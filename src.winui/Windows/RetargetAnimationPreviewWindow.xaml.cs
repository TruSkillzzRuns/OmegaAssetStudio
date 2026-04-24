using System.Collections.ObjectModel;
using System.Numerics;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.Retargeting;
using OmegaAssetStudio.WinUI.Rendering;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using UpkManager.Models.UpkFile.Engine.Anim;
using Point = global::Windows.Foundation.Point;

namespace OmegaAssetStudio.WinUI.Popouts;

public sealed partial class RetargetAnimationPreviewWindow : Window
{
    private readonly RetargetPosePreviewService _posePreviewService = new();
    private readonly RetargetAnimationPlaybackService _playbackService = new();
    private readonly RetargetToPreviewMeshConverter _previewConverter = new();
    private readonly MeshPreviewD3D11Renderer _renderer = new();
    private readonly MeshPreviewSoftwareRenderer _softwareRenderer = new();
    private readonly MeshPreviewScene _scene = new();
    private readonly MeshPreviewCamera _camera = new();
    private readonly DispatcherQueueTimer _playbackTimer;
    private readonly ObservableCollection<PlaybackSequenceItem> _sequenceItems = [];
    private RetargetMesh? _previewSourceMesh;
    private UAnimSet? _previewAnimSet;
    private RetargetAnimationSequenceInfo? _selectedSequence;
    private MeshPreviewMesh? _previewMesh;
    private bool _surfaceLoaded;
    private bool _pointerCaptured;
    private bool _panMode;
    private bool _renderInProgress;
    private bool _renderPending;
    private bool _suppressPlaybackUpdate;
    private bool _playbackActive;
    private bool _resetCameraOnNextRender = true;
    private Point _lastPointerPoint;
    private RetargetPosePreset _selectedPosePreset = RetargetPosePreset.BindPose;
    private bool _showingPosedMesh;
    private bool _disposed;
    private float _playbackTimeSeconds;
    private DateTimeOffset _lastPlaybackTick;

    public RetargetAnimationPreviewWindow()
    {
        InitializeComponent();
        Closed += RetargetAnimationPreviewWindow_Closed;
        _playbackTimer = DispatcherQueue.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playbackTimer.IsRepeating = true;
        _playbackTimer.Tick += PlaybackTimer_Tick;
        SequenceComboBox.ItemsSource = _sequenceItems;
        InitializePreview();
    }

    public void SetPreviewSource(RetargetMesh? sourceMesh, UAnimSet? animSet, RetargetPosePreset preset)
    {
        if (_disposed)
            return;

        _previewSourceMesh = sourceMesh?.DeepClone();
        _previewAnimSet = animSet ?? _previewSourceMesh?.AnimSet;
        _selectedPosePreset = preset;
        _showingPosedMesh = false;
        _playbackActive = false;
        _playbackTimeSeconds = 0.0f;
        _selectedSequence = null;
        _resetCameraOnNextRender = true;
        UpdatePosePresetSelection();
        LoadAnimSetSequences();
        UpdatePreviewSourceText();
        UpdatePlaybackUiState();
        PreviewStatusText.Text = _previewSourceMesh is null
            ? "Animation preview is idle."
            : _previewSourceMesh.Bones.Count == 0
                ? "Animation preview loaded. No rig bones were found, so a static fallback is being used."
                : _selectedSequence is null
                    ? "Animation preview loaded. Select a sequence to play back the UPK animation."
                    : "Animation preview loaded. Use Play to preview the selected sequence.";
        _ = RefreshPreviewAsync();
    }

    private void InitializePreview()
    {
        PosePresetComboBox.SelectedIndex = 0;
        _scene.DisplayMode = MeshPreviewDisplayMode.FbxOnly;
        _scene.ShowFbxMesh = true;
        _scene.ShowUe3Mesh = false;
        _scene.Wireframe = false;
        _scene.ShowBones = false;
        _scene.ShowWeights = false;
        _scene.ShowSections = false;
        _scene.ShowNormals = false;
        _scene.ShowTangents = false;
        _scene.ShowUvSeams = false;
        _scene.MaterialPreviewEnabled = false;
        _scene.ShadingMode = MeshPreviewShadingMode.Lit;
        _scene.BackgroundStyle = MeshPreviewBackgroundStyle.DarkGradient;
        _scene.LightingPreset = MeshPreviewLightingPreset.Neutral;
        _scene.ShowGroundPlane = true;
        PreviewStatsText.Text = "Idle";
        PreviewStatusText.Text = "Load a source mesh from the Retarget workspace to preview poses here.";
        AnimSetStatusText.Text = "No AnimSet loaded.";
        PlaybackTimeText.Text = "0.000 s";
        PlaybackTimeSlider.Minimum = 0;
        PlaybackTimeSlider.Maximum = 1;
        PlaybackTimeSlider.Value = 0;
        PlayPauseButton.Content = "Play";
    }

    private void UpdatePosePresetSelection()
    {
        foreach (object item in PosePresetComboBox.Items)
        {
            if (item is ComboBoxItem comboItem && string.Equals(comboItem.Content?.ToString(), _selectedPosePreset.ToString(), StringComparison.Ordinal))
            {
                PosePresetComboBox.SelectedItem = comboItem;
                return;
            }
        }
    }

    private async void PreviewSurface_Loaded(object sender, RoutedEventArgs e)
    {
        _surfaceLoaded = true;
        await RefreshPreviewAsync().ConfigureAwait(true);
    }

    private async void PreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        _surfaceLoaded = true;
        await RefreshPreviewAsync().ConfigureAwait(true);
    }

    private async void PreviewViewportHost_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_previewMesh is null)
            return;

        _pointerCaptured = true;
        var properties = e.GetCurrentPoint(PreviewViewportHost).Properties;
        _panMode = properties.IsRightButtonPressed || properties.IsMiddleButtonPressed;
        _lastPointerPoint = e.GetCurrentPoint(PreviewViewportHost).Position;
        PreviewViewportHost.CapturePointer(e.Pointer);
        await Task.CompletedTask;
    }

    private async void PreviewViewportHost_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_pointerCaptured || _previewMesh is null)
            return;

        var currentPoint = e.GetCurrentPoint(PreviewViewportHost);
        var currentPosition = currentPoint.Position;
        float deltaX = (float)(currentPosition.X - _lastPointerPoint.X);
        float deltaY = (float)(currentPosition.Y - _lastPointerPoint.Y);
        _lastPointerPoint = currentPosition;

        if (_panMode)
            _camera.Pan(deltaX, -deltaY);
        else
            _camera.Orbit(deltaX, deltaY);

        await RenderPreviewAsync().ConfigureAwait(true);
    }

    private async void PreviewViewportHost_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_pointerCaptured)
            return;

        _pointerCaptured = false;
        _panMode = false;
        PreviewViewportHost.ReleasePointerCapture(e.Pointer);
        await RenderPreviewAsync().ConfigureAwait(true);
    }

    private async void PreviewViewportHost_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_previewMesh is null)
            return;

        float delta = e.GetCurrentPoint(PreviewViewportHost).Properties.MouseWheelDelta / 120.0f;
        _camera.Zoom(delta);
        await RenderPreviewAsync().ConfigureAwait(true);
    }

    private async void PosePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PosePresetComboBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse(item.Content?.ToString(), out RetargetPosePreset preset))
        {
            _selectedPosePreset = preset;
            if (_showingPosedMesh)
                await RefreshPreviewAsync().ConfigureAwait(true);
        }
    }

    private async void ApplyPoseButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyPosePreviewAsync().ConfigureAwait(true);
    }

    private async void ResetPoseButton_Click(object sender, RoutedEventArgs e)
    {
        await ResetPosePreviewAsync().ConfigureAwait(true);
    }

    private async void ResetCameraButton_Click(object sender, RoutedEventArgs e)
    {
        await ResetCameraAsync().ConfigureAwait(true);
    }

    private async Task ApplyPosePreviewAsync()
    {
        if (_previewSourceMesh is null)
        {
            PreviewStatusText.Text = "Load a source mesh first.";
            return;
        }

        if (_selectedSequence is not null)
        {
            PreviewStatusText.Text = "Clear the selected sequence to use pose preview.";
            return;
        }

        _showingPosedMesh = true;
        PreviewStatusText.Text = $"Applying {_selectedPosePreset}...";
        _resetCameraOnNextRender = true;
        await RefreshPreviewAsync().ConfigureAwait(true);
    }

    private async Task ResetPosePreviewAsync()
    {
        if (_previewSourceMesh is null)
        {
            PreviewStatusText.Text = "Load a source mesh first.";
            return;
        }

        if (_selectedSequence is not null)
        {
            PreviewStatusText.Text = "Clear the selected sequence to return to bind pose preview.";
            return;
        }

        _showingPosedMesh = false;
        PreviewStatusText.Text = "Showing bind pose.";
        _resetCameraOnNextRender = true;
        await RefreshPreviewAsync().ConfigureAwait(true);
    }

    private async Task ResetCameraAsync()
    {
        if (_previewMesh is null && _previewSourceMesh is null)
            return;

        MeshPreviewMesh focusMesh = _previewMesh ?? _scene.FbxMesh ?? _scene.Ue3Mesh;
        if (focusMesh is null)
            _camera.Reset(Vector3.Zero, 10.0f);
        else
            _camera.Reset(focusMesh.Center, MathF.Max(1.0f, focusMesh.Radius));

        await RenderPreviewAsync().ConfigureAwait(true);
    }

    private void PlaybackTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_playbackActive || _selectedSequence is null || _previewSourceMesh is null || _previewAnimSet is null)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan delta = now - _lastPlaybackTick;
        _lastPlaybackTick = now;
        if (delta <= TimeSpan.Zero)
            return;

        float duration = _selectedSequence.Sequence.SequenceLength > 1e-5f
            ? _selectedSequence.Sequence.SequenceLength
            : Math.Max(1, _selectedSequence.Sequence.NumFrames - 1) / 30.0f;
        if (duration <= 1e-5f)
            duration = 1.0f / 30.0f;

        _playbackTimeSeconds += (float)delta.TotalSeconds * Math.Max(0.0f, _selectedSequence.Sequence.RateScale);
        if (_playbackTimeSeconds > duration)
            _playbackTimeSeconds %= duration;

        UpdatePlaybackUiState();
        _ = RefreshPreviewAsync();
    }

    private void LoadAnimSetSequences()
    {
        _sequenceItems.Clear();
        if (_previewAnimSet is null)
        {
            AnimSetStatusText.Text = "No AnimSet loaded.";
            _selectedSequence = null;
            return;
        }

        IReadOnlyList<RetargetAnimationSequenceInfo> sequences = _playbackService.GetSequences(_previewAnimSet, AppendLog);
        foreach (RetargetAnimationSequenceInfo sequence in sequences)
            _sequenceItems.Add(new PlaybackSequenceItem(sequence));

        AnimSetStatusText.Text = sequences.Count == 0
            ? "AnimSet loaded, but no AnimSequence exports were found."
            : $"AnimSet loaded with {sequences.Count:N0} sequence(s).";

        _suppressPlaybackUpdate = true;
        SequenceComboBox.SelectedIndex = _sequenceItems.Count > 0 ? 0 : -1;
        _suppressPlaybackUpdate = false;

        _selectedSequence = _sequenceItems.Count > 0 ? _sequenceItems[0].SequenceInfo : null;
        if (_selectedSequence is not null)
        {
            float duration = _selectedSequence.Sequence.SequenceLength > 1e-5f
                ? _selectedSequence.Sequence.SequenceLength
                : Math.Max(1, _selectedSequence.Sequence.NumFrames - 1) / 30.0f;
            PlaybackTimeSlider.Minimum = 0;
            PlaybackTimeSlider.Maximum = Math.Max(0.01, duration);
        }

        UpdatePlaybackUiState();
    }

    private void UpdatePreviewSourceText()
    {
        if (_previewSourceMesh is null)
        {
            PreviewSourceText.Text = "Load a source mesh from the Retarget workspace to preview poses here.";
            return;
        }

        string animSetText = _previewAnimSet is null
            ? "AnimSet: none"
            : $"AnimSet: {(_sequenceItems.Count > 0 ? _sequenceItems.Count.ToString("N0") : "0")} sequence(s)";

        PreviewSourceText.Text = $"Source: {_previewSourceMesh.MeshName} | Vertices: {_previewSourceMesh.VertexCount:N0} | Triangles: {_previewSourceMesh.TriangleCount:N0} | Bones: {_previewSourceMesh.Bones.Count:N0} | {animSetText}";
    }

    private void UpdatePlaybackUiState()
    {
        if (_selectedSequence is null)
        {
            PlaybackTimeText.Text = "0.000 s";
            _suppressPlaybackUpdate = true;
            PlaybackTimeSlider.Value = 0;
            _suppressPlaybackUpdate = false;
            PlayPauseButton.Content = "Play";
            return;
        }

        float duration = _selectedSequence.Sequence.SequenceLength > 1e-5f
            ? _selectedSequence.Sequence.SequenceLength
            : Math.Max(1, _selectedSequence.Sequence.NumFrames - 1) / 30.0f;
        if (duration <= 1e-5f)
            duration = 1.0f / 30.0f;

        float time = Math.Clamp(_playbackTimeSeconds, 0.0f, duration);
        PlaybackTimeText.Text = $"{time:0.000} s / {duration:0.000} s";
        _suppressPlaybackUpdate = true;
        PlaybackTimeSlider.Minimum = 0;
        PlaybackTimeSlider.Maximum = Math.Max(0.01, duration);
        PlaybackTimeSlider.Value = time;
        _suppressPlaybackUpdate = false;
        PlayPauseButton.Content = _playbackActive ? "Pause" : "Play";
    }

    private void SequenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaybackUpdate)
            return;

        if (SequenceComboBox.SelectedItem is not PlaybackSequenceItem item)
        {
            _selectedSequence = null;
            _playbackTimeSeconds = 0.0f;
            UpdatePlaybackUiState();
            _ = RefreshPreviewAsync();
            return;
        }

        _selectedSequence = item.SequenceInfo;
        _playbackTimeSeconds = 0.0f;
        _playbackActive = false;
        _showingPosedMesh = false;
        _resetCameraOnNextRender = true;
        _lastPlaybackTick = DateTimeOffset.UtcNow;
        _playbackTimer.Stop();
        UpdatePlaybackUiState();
        _ = RefreshPreviewAsync();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSequence is null)
        {
            PreviewStatusText.Text = "Select an AnimSet sequence first.";
            return;
        }

        _playbackActive = !_playbackActive;
        _showingPosedMesh = false;
        if (_playbackActive)
        {
            _lastPlaybackTick = DateTimeOffset.UtcNow;
            _playbackTimer.Start();
        }
        else
        {
            _playbackTimer.Stop();
        }

        UpdatePlaybackUiState();
        PreviewStatusText.Text = _playbackActive
            ? $"Playing {_selectedSequence.DisplayName}."
            : $"Paused {_selectedSequence.DisplayName}.";
    }

    private void StopPlaybackButton_Click(object sender, RoutedEventArgs e)
    {
        _playbackActive = false;
        _playbackTimeSeconds = 0.0f;
        _playbackTimer.Stop();
        _resetCameraOnNextRender = true;
        UpdatePlaybackUiState();
        _ = RefreshPreviewAsync();
    }

    private void ClearSequenceButton_Click(object sender, RoutedEventArgs e)
    {
        _playbackActive = false;
        _selectedSequence = null;
        _playbackTimeSeconds = 0.0f;
        _playbackTimer.Stop();
        _resetCameraOnNextRender = true;
        _suppressPlaybackUpdate = true;
        SequenceComboBox.SelectedIndex = -1;
        _suppressPlaybackUpdate = false;
        UpdatePlaybackUiState();
        _ = RefreshPreviewAsync();
    }

    private void PlaybackTimeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressPlaybackUpdate || _selectedSequence is null || !_surfaceLoaded)
            return;

        _playbackTimeSeconds = (float)e.NewValue;
        UpdatePlaybackUiState();
        _ = RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_renderInProgress)
        {
            _renderPending = true;
            return;
        }

        _renderInProgress = true;
        try
        {
            do
            {
                _renderPending = false;

                if (_previewSourceMesh is null)
                {
                    ClearPreview();
                    return;
                }

                RetargetMesh workingMesh;
                RetargetAnimationSequenceInfo? selectedSequence = _selectedSequence;
                UAnimSet? previewAnimSet = _previewAnimSet;
                bool canPlaySequence = selectedSequence is not null &&
                    previewAnimSet is not null &&
                    _previewSourceMesh.Bones.Count > 0;

                if (selectedSequence is not null && !canPlaySequence)
                {
                    _playbackActive = false;
                    _playbackTimer.Stop();
                    workingMesh = _previewSourceMesh.DeepClone();
                }
                else if (canPlaySequence)
                {
                    workingMesh = await Task.Run(() => _playbackService.ApplySequence(_previewSourceMesh, previewAnimSet!, selectedSequence!.Sequence, _playbackTimeSeconds, AppendLog)).ConfigureAwait(true);
                }
                else if (_showingPosedMesh)
                {
                    workingMesh = await Task.Run(() => _posePreviewService.ApplyPose(_previewSourceMesh, _selectedPosePreset, AppendLog)).ConfigureAwait(true);
                }
                else
                {
                    workingMesh = _previewSourceMesh.DeepClone();
                }

                bool hadPreviewMesh = _previewMesh is not null;
                MeshPreviewMesh previewMesh = await Task.Run(() => _previewConverter.Convert(workingMesh, workingMesh.MeshName, AppendLog)).ConfigureAwait(true);
                _previewMesh = previewMesh;
                _scene.SetFbxMesh(previewMesh);
                _scene.SetUe3Mesh(null);
                if (_resetCameraOnNextRender || !hadPreviewMesh)
                {
                    _camera.Reset(previewMesh.Center, MathF.Max(1.0f, previewMesh.Radius));
                    _resetCameraOnNextRender = false;
                }

                await RenderPreviewAsync().ConfigureAwait(true);
                PreviewStatsText.Text = BuildStatsText();
                if (selectedSequence is not null)
                    PreviewStatusText.Text = canPlaySequence
                        ? $"{(_playbackActive ? "Playing" : "Paused")} {selectedSequence.DisplayName} at {_playbackTimeSeconds:0.000}s."
                        : "Selected sequence is available, but the source mesh has no bones to animate.";
                else
                    PreviewStatusText.Text = _showingPosedMesh
                        ? $"Showing {_selectedPosePreset} on {_previewMesh.Name}."
                        : $"Showing bind pose on {_previewMesh.Name}.";
            }
            while (_renderPending);
        }
        catch (Exception ex)
        {
            PreviewStatusText.Text = $"Preview failed: {ex.Message}";
            ClearPreview();
        }
        finally
        {
            _renderInProgress = false;
        }
    }

    private async Task RenderPreviewAsync()
    {
        if (_previewMesh is null)
            return;

        int width = (int)Math.Max(320, PreviewSwapChainPanel.ActualWidth > 0 ? PreviewSwapChainPanel.ActualWidth : (PreviewImage.ActualWidth > 0 ? PreviewImage.ActualWidth : 960));
        int height = (int)Math.Max(240, PreviewSwapChainPanel.ActualHeight > 0 ? PreviewSwapChainPanel.ActualHeight : (PreviewImage.ActualHeight > 0 ? PreviewImage.ActualHeight : 540));
        bool forceSoftwareRender = _selectedSequence is not null;

        if (forceSoftwareRender)
        {
            _renderer.DetachPanel();
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewImage.Source = _softwareRenderer.Render(
                _scene,
                width,
                height,
                _camera,
                _scene.ShadingMode,
                _scene.BackgroundStyle,
                _scene.LightingPreset,
                _scene.Wireframe,
                _scene.ShowGroundPlane);
            return;
        }

        if (_surfaceLoaded && PreviewSwapChainPanel.XamlRoot is not null)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Visible;
            _renderer.AttachToPanel(PreviewSwapChainPanel, DispatcherQueue);
            _renderer.SetFrame(_scene, _camera);
            if (!_renderer.LastRenderSucceeded)
            {
                PreviewStatusText.Text = _renderer.Diagnostics;
                PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Visible;
                WriteableBitmap bitmap = _softwareRenderer.Render(
                    _scene,
                    width,
                    height,
                    _camera,
                    _scene.ShadingMode,
                    _scene.BackgroundStyle,
                    _scene.LightingPreset,
                    _scene.Wireframe,
                    _scene.ShowGroundPlane);
                PreviewImage.Source = bitmap;
            }
        }
        else
        {
            _renderer.DetachPanel();
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;
            WriteableBitmap bitmap = _softwareRenderer.Render(
                _scene,
                width,
                height,
                _camera,
                _scene.ShadingMode,
                _scene.BackgroundStyle,
                _scene.LightingPreset,
                _scene.Wireframe,
                _scene.ShowGroundPlane);
            PreviewImage.Source = bitmap;
        }

        await Task.CompletedTask;
    }

    private void ClearPreview()
    {
        _previewMesh = null;
        _scene.Clear();
        _renderer.DetachPanel();
        PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewStatsText.Text = "Idle";
        PreviewStatusText.Text = "Load a source mesh from the Retarget workspace to preview poses here.";
    }

    private string BuildStatsText()
    {
        if (_previewMesh is null)
            return "Idle";

        return $"Ready  |  Verts: {_previewMesh.Vertices.Count:N0}  |  Tris: {_previewMesh.Indices.Count / 3:N0}  |  Bones: {_previewMesh.Bones.Count:N0}";
    }

    private void AppendLog(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            PreviewStatusText.Text = message;
    }

    private void RetargetAnimationPreviewWindow_Closed(object sender, WindowEventArgs args)
    {
        _disposed = true;
        _playbackTimer.Stop();
        _renderer.DetachPanel();
        _scene.Clear();
        _renderer.Dispose();
    }

    private sealed class PlaybackSequenceItem
    {
        public PlaybackSequenceItem(RetargetAnimationSequenceInfo sequenceInfo)
        {
            SequenceInfo = sequenceInfo;
        }

        public RetargetAnimationSequenceInfo SequenceInfo { get; }
        public string DisplayName => SequenceInfo.DisplayName;
    }
}

