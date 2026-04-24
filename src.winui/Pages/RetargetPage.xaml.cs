using System.Collections.ObjectModel;
using System.Numerics;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.WinUI.Rendering;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.WinUI.Modules.Workflows;
using OmegaAssetStudio.Retargeting;
using OmegaAssetStudio.WinUI.Popouts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using UpkManager.Models.UpkFile.Engine.Anim;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;
using Windows.Storage.Pickers;
using WinRT.Interop;
using RetargetMeshImporter = OmegaAssetStudio.Retargeting.MeshImporter;
using Point = global::Windows.Foundation.Point;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class RetargetPage : Page
{
    private readonly UpkFileRepository _repository = new();
    private readonly MhoSkeletalMeshConverter _referenceMeshConverter = new();
    private UnrealHeader? _currentHeader;
    private string? _currentUpkPath;
    private RetargetMesh? _referenceMesh;
    private SkeletonDefinition? _referenceSkeleton;
    private RetargetMesh? _sourceMesh;
    private RetargetMesh? _processedMesh;
    private SkeletonDefinition? _playerSkeleton;
    private UAnimSet? _animSet;
    private string? _animSetDisplayName;
    private readonly List<string> _texturePaths = [];
    private BoneMappingResult? _boneMapping;
    private readonly RetargetMappingService _mappingService = new();
    private readonly SkeletonValidatorService _validatorService = new();
    private readonly PoseAlignmentService _poseAlignmentService = new();
    private readonly MirrorFixService _mirrorFixService = new();
    private readonly NotifyTransferService _notifyTransferService = new();
    private readonly CurveTransferService _curveTransferService = new();
    private readonly BatchRetargetService _batchRetargetService = new();
    private readonly RetargetPosePreviewService _posePreviewService = new();
    private readonly RetargetToPreviewMeshConverter _retargetPreviewConverter = new();
    private readonly TransformSolverService _transformSolverService = new();
    private readonly MeshPreviewSoftwareRenderer _retargetPreviewSoftwareRenderer = new();
    private MeshPreviewD3D11Renderer _retargetPreviewRenderer = new();
    private readonly MeshPreviewScene _retargetPreviewScene = new();
    private readonly MeshPreviewCamera _retargetPreviewCamera = new();
    private RetargetPosePreset _selectedPosePreset = RetargetPosePreset.BindPose;
    private bool _suppressLodRefresh;
    private bool _suppressExportSelectionInspect;
    private int _selectedLodIndex;
    private bool _busy;
    private WorkspaceLaunchContext? _launchContext;
    private RetargetAnimationPreviewWindow? _animationPreviewWindow;
    private string? _selectedExportPath;
    private string? _batchFolderPath;
    private MeshPreviewMesh? _retargetPreviewMesh;
    private bool _retargetPreviewSurfaceLoaded;
    private bool _retargetPreviewPointerCaptured;
    private bool _retargetPreviewPanMode;
    private Point _retargetPreviewLastPointerPoint;
    private bool _retargetPreviewRenderInProgress;
    private bool _retargetPreviewRenderPending;
    private readonly Dictionary<string, string> _manualMappingOverrides = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSessionWrites;

    public ObservableCollection<RetargetExportViewModel> ExportEntries { get; } = [];
    public ObservableCollection<RetargetDetailRow> DetailRows { get; } = [];
    public ObservableCollection<RetargetDetailRow> MappingRows { get; } = [];
    public ObservableCollection<RetargetDetailRow> ValidationRows { get; } = [];
    public ObservableCollection<RetargetDetailRow> AnimationTransferRows { get; } = [];
    public ObservableCollection<RetargetDetailRow> BatchRetargetRows { get; } = [];
    public ObservableCollection<RetargetDetailRow> MirrorFixRows { get; } = [];
    public ObservableCollection<RetargetDetailRow> TransformSolverRows { get; } = [];

    public RetargetPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        ExportListView.ItemsSource = ExportEntries;
        DetailListView.ItemsSource = DetailRows;
        MappingListView.ItemsSource = MappingRows;
        ValidationListView.ItemsSource = ValidationRows;
        AnimationTransferListView.ItemsSource = AnimationTransferRows;
        BatchRetargetListView.ItemsSource = BatchRetargetRows;
        MirrorFixListView.ItemsSource = MirrorFixRows;
        TransformSolverListView.ItemsSource = TransformSolverRows;
        InitializeRetargetPreview();
        _suppressSessionWrites = true;
        ApplySessionState();
        _suppressSessionWrites = false;
        SetEmptyState();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _launchContext = e.Parameter as WorkspaceLaunchContext;
        if (_launchContext is not null && !string.IsNullOrWhiteSpace(_launchContext.UpkPath))
        {
            UpkPathTextBox.Text = _launchContext.UpkPath;
            _ = LoadUpkAsync(_launchContext.UpkPath);
            return;
        }

        TryRestoreSession();
    }

    private async void BrowseUpkButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        UpkPathTextBox.Text = file.Path;
        await LoadUpkAsync(file.Path).ConfigureAwait(true);
    }

    private async void LoadSelectedUpkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UpkPathTextBox.Text))
        {
            RetargetStatusText.Text = "Choose a UPK first.";
            return;
        }

        await LoadUpkAsync(UpkPathTextBox.Text).ConfigureAwait(true);
    }

    private async void UseCurrentUpkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentUpkPath))
        {
            RetargetStatusText.Text = "No current UPK is loaded.";
            return;
        }

        UpkPathTextBox.Text = _currentUpkPath;
        await LoadUpkAsync(_currentUpkPath).ConfigureAwait(true);
    }

    private async void ExportListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressExportSelectionInspect)
            return;

        if (ExportListView.SelectedItem is not RetargetExportViewModel selected)
            return;

        await InspectExportAsync(selected).ConfigureAwait(true);
    }

    private async void ImportSourceMeshButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".fbx");
        picker.FileTypeFilter.Add(".psk");
        picker.FileTypeFilter.Add(".obj");
        picker.FileTypeFilter.Add(".dae");
        picker.FileTypeFilter.Add(".gltf");
        picker.FileTypeFilter.Add(".glb");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        await ImportSourceMeshAsync(file.Path).ConfigureAwait(true);
    }

    private void ResetSourceButton_Click(object sender, RoutedEventArgs e)
    {
        _sourceMesh = null;
        _processedMesh = null;
        _boneMapping = null;
        _manualMappingOverrides.Clear();
        AnimationTransferRows.Clear();
        BatchRetargetRows.Clear();
        MirrorFixRows.Clear();
        TransformSolverRows.Clear();
        _batchFolderPath = null;
        _selectedExportPath = null;
        SourceMeshStatusText.Text = "Import a source mesh to begin retargeting.";
        SourceMeshSummaryText.Text = "No source mesh loaded.";
        WorkflowStatusText.Text = "Import a source mesh, then run map/scale/transfer.";
        RetargetSummaryText.Text = "Retarget inspection details will appear here once a SkeletalMesh export is selected.";
        ClearRetargetPreview();
        RefreshMappingRows();
        ValidationRows.Clear();
        ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
        AnimationTransferStatusText.Text = "Transfer notifies and curves from the current AnimSet into the retargeted output.";
        BatchRetargetStatusText.Text = "Choose a folder of UPKs and retarget each one using the current mesh and export selection.";
        MirrorFixStatusText.Text = "Run Auto Fix Mirroring when the source mesh appears flipped or backfacing.";
        TransformSolverStatusText.Text = "Run Stabilize Transforms to clean unstable matrices before transfer and export.";
        BatchFolderTextBox.Text = string.Empty;
        BatchRetargetLogText.Text = "Batch log will be written after the run completes.";
        UpdateRetargetContextSummary();
    }

    private async void AutoMapBonesButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoMapBonesAsync().ConfigureAwait(true);
    }

    private async void AutoOrientButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoOrientSourceAsync().ConfigureAwait(true);
    }

    private async void AutoScaleButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoScaleSourceAsync().ConfigureAwait(true);
    }

    private async void AutoAlignPoseButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoAlignPoseAsync().ConfigureAwait(true);
    }

    private async void AutoFixMirrorButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoFixMirrorAsync().ConfigureAwait(true);
    }

    private async void SolveTransformsButton_Click(object sender, RoutedEventArgs e)
    {
        await SolveTransformsAsync().ConfigureAwait(true);
    }

    private async void ValidateSkeletonButton_Click(object sender, RoutedEventArgs e)
    {
        await ValidateSkeletonAsync().ConfigureAwait(true);
    }

    private async void TransferNotifiesButton_Click(object sender, RoutedEventArgs e)
    {
        await TransferAnimSetMetadataAsync(includeNotifies: true, includeCurves: false).ConfigureAwait(true);
    }

    private async void TransferCurvesButton_Click(object sender, RoutedEventArgs e)
    {
        await TransferAnimSetMetadataAsync(includeNotifies: false, includeCurves: true).ConfigureAwait(true);
    }

    private async void SelectBatchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await SelectBatchFolderAsync().ConfigureAwait(true);
    }

    private async void BatchRetargetButton_Click(object sender, RoutedEventArgs e)
    {
        await BatchRetargetAsync().ConfigureAwait(true);
    }

    private async void TransferWeightsButton_Click(object sender, RoutedEventArgs e)
    {
        await TransferWeightsAsync().ConfigureAwait(true);
    }

    private async void OneClickRetargetButton_Click(object sender, RoutedEventArgs e)
    {
        await OneClickRetargetAsync().ConfigureAwait(true);
    }

    private async void ImportSkeletonButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportRetargetSkeletonAsync().ConfigureAwait(true);
    }

    private async void ImportAnimSetButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportRetargetAnimSetAsync().ConfigureAwait(true);
    }

    private async void ImportTexturesButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportRetargetTexturesAsync().ConfigureAwait(true);
    }

    private async void ApplyUe3FixesButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyUe3FixesAsync().ConfigureAwait(true);
    }

    private async void ExportFbxButton_Click(object sender, RoutedEventArgs e)
    {
        await ExportRetargetFbxAsync().ConfigureAwait(true);
    }

    private async void ReplaceMeshButton_Click(object sender, RoutedEventArgs e)
    {
        await ReplaceRetargetMeshAsync().ConfigureAwait(true);
    }

    private void PosePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PosePresetComboBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse(item.Content?.ToString(), out RetargetPosePreset preset))
        {
            _selectedPosePreset = preset;
            SaveSessionState();
        }
    }

    private async void LodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLodRefresh)
            return;

        _selectedLodIndex = LodComboBox.SelectedIndex < 0 ? 0 : LodComboBox.SelectedIndex;
        UpdateRetargetContextSummary();
        SaveSessionState();

        if (ExportListView.SelectedItem is RetargetExportViewModel selected)
            await InspectExportAsync(selected).ConfigureAwait(true);
    }

    private async void ApplyPosePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyPosePreviewAsync().ConfigureAwait(true);
    }

    private async void ResetPosePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await ResetPosePreviewAsync().ConfigureAwait(true);
    }

    private void OpenAnimationPreviewWindowButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAnimationPreviewWindow();
    }

    private async void ResetRetargetPreviewCameraButton_Click(object sender, RoutedEventArgs e)
    {
        await ResetRetargetPreviewCameraAsync().ConfigureAwait(true);
    }

    private void WorkflowsButton_Click(object sender, RoutedEventArgs e)
    {
        WorkflowsService.OpenWorkflowsWindow();
    }

    private async void SaveMappingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveMappingProfileAsync().ConfigureAwait(true);
    }

    private async void LoadMappingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadMappingProfileAsync().ConfigureAwait(true);
    }

    private async void ApplyManualOverrideButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyManualOverrideAsync().ConfigureAwait(true);
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMesh(-90.0f);
    }

    private void RotateRightButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMesh(90.0f);
    }

    private void RotateFlipButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMesh(180.0f);
    }

    private void RotatePitchForwardButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMeshAroundAxis("forward", -90.0f);
    }

    private void RotatePitchBackwardButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMeshAroundAxis("forward", 90.0f);
    }

    private void RotateRollLeftButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMeshAroundAxis("right", -90.0f);
    }

    private void RotateRollRightButton_Click(object sender, RoutedEventArgs e)
    {
        RotateRetargetSourceMeshAroundAxis("right", 90.0f);
    }

    private async void RetargetPreviewSurface_Loaded(object sender, RoutedEventArgs e)
    {
        _retargetPreviewSurfaceLoaded = true;
        await RefreshRetargetPreviewAsync().ConfigureAwait(true);
    }

    private async void RetargetPreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        _retargetPreviewSurfaceLoaded = true;
        await RefreshRetargetPreviewAsync().ConfigureAwait(true);
    }

    private void RetargetPreviewViewportHost_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_retargetPreviewMesh is null)
            return;

        _retargetPreviewPointerCaptured = true;
        var properties = e.GetCurrentPoint(RetargetPreviewViewportHost).Properties;
        _retargetPreviewPanMode = properties.IsRightButtonPressed || properties.IsMiddleButtonPressed;
        _retargetPreviewLastPointerPoint = e.GetCurrentPoint(RetargetPreviewViewportHost).Position;
        RetargetPreviewViewportHost.CapturePointer(e.Pointer);
    }

    private async void RetargetPreviewViewportHost_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_retargetPreviewPointerCaptured || _retargetPreviewMesh is null)
            return;

        var currentPoint = e.GetCurrentPoint(RetargetPreviewViewportHost);
        var currentPosition = currentPoint.Position;
        float deltaX = (float)(currentPosition.X - _retargetPreviewLastPointerPoint.X);
        float deltaY = (float)(currentPosition.Y - _retargetPreviewLastPointerPoint.Y);
        _retargetPreviewLastPointerPoint = currentPosition;

        if (_retargetPreviewPanMode)
            _retargetPreviewCamera.Pan(deltaX, -deltaY);
        else
            _retargetPreviewCamera.Orbit(deltaX, deltaY);

        await RenderRetargetPreviewAsync().ConfigureAwait(true);
    }

    private async void RetargetPreviewViewportHost_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_retargetPreviewPointerCaptured)
            return;

        _retargetPreviewPointerCaptured = false;
        _retargetPreviewPanMode = false;
        RetargetPreviewViewportHost.ReleasePointerCapture(e.Pointer);
        await RenderRetargetPreviewAsync().ConfigureAwait(true);
    }

    private async void RetargetPreviewViewportHost_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_retargetPreviewMesh is null)
            return;

        float delta = e.GetCurrentPoint(RetargetPreviewViewportHost).Properties.MouseWheelDelta / 120.0f;
        _retargetPreviewCamera.Zoom(delta);
        await RenderRetargetPreviewAsync().ConfigureAwait(true);
    }

    private async Task LoadUpkAsync(string upkPath)
    {
        if (_busy)
            return;

        SetBusy(true, $"Loading {Path.GetFileName(upkPath)}...");
        SetEmptyState();
        UpkStatusText.Text = $"Loading {Path.GetFileName(upkPath)}...";

        try
        {
            _currentUpkPath = upkPath;
            _selectedExportPath = null;
            ReportProgress(10, 100, "Reading UPK header.");
            _currentHeader = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
            await _currentHeader.ReadHeaderAsync(null).ConfigureAwait(true);
            ReportProgress(55, 100, "Scanning SkeletalMesh exports.");

            _referenceMesh = null;
            _referenceSkeleton = null;
            ExportEntries.Clear();
            foreach (UnrealExportTableEntry export in _currentHeader.ExportTable)
            {
                string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
                if (!className.Equals("SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                    continue;

                ExportEntries.Add(new RetargetExportViewModel
                {
                    TableIndex = export.TableIndex,
                    DisplayName = export.GetPathName(),
                    PathName = export.GetPathName(),
                    ClassName = className,
                    MeshSummary = "SkeletalMesh export"
                });
            }

            if (ExportEntries.Count == 0)
            {
                RetargetTitleText.Text = "No SkeletalMesh exports found.";
                RetargetStatusText.Text = "This UPK does not contain a SkeletalMesh export.";
                UpkStatusText.Text = $"Loaded {Path.GetFileName(upkPath)}.";
                RetargetSummaryText.Text = "Select another UPK if you want to inspect retargetable meshes.";
                DetailRows.Clear();
                return;
            }

            RetargetTitleText.Text = $"{ExportEntries.Count} SkeletalMesh export(s) found.";
            RetargetStatusText.Text = "Select a mesh to inspect its bones and LODs.";
            UpkStatusText.Text = $"Loaded {Path.GetFileName(upkPath)}.";
            RetargetSummaryText.Text = "Export list populated.";
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            await SelectAndInspectPreferredExportAsync().ConfigureAwait(true);
            QueueRetargetPreviewRefresh();
            ReportProgress(100, 100, "UPK loaded.");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            RetargetTitleText.Text = "Retarget load failed.";
            RetargetStatusText.Text = $"Failed to load the selected UPK for retargeting while reading the package data: {ex.Message}";
            UpkStatusText.Text = "UPK load failed.";
            SaveSessionState();
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task InspectExportAsync(RetargetExportViewModel selected)
    {
        if (_currentHeader is null)
            return;

        try
        {
            UnrealExportTableEntry? export = _currentHeader.ExportTable.FirstOrDefault(entry => entry.TableIndex == selected.TableIndex);
            if (export is null)
                return;

            _selectedExportPath = export.GetPathName();

            if (export.UnrealObject is null)
                await export.ParseUnrealObject(false, false).ConfigureAwait(true);

            if (export.UnrealObject is not IUnrealObject wrapper || wrapper.UObject is not USkeletalMesh skeletalMesh)
            {
                RetargetStatusText.Text = "Selected export is not a SkeletalMesh.";
                return;
            }

            _referenceSkeleton = BuildSkeletonDefinitionFromSkeletalMesh(export.GetPathName(), skeletalMesh);
            _playerSkeleton = _referenceSkeleton;
            _referenceMesh = null;
            _boneMapping = null;
            _manualMappingOverrides.Clear();
            int lodCount = skeletalMesh.LODModels?.Count ?? 0;
            SetLodOptions(lodCount);
            try
            {
                if (lodCount > 0)
                    _referenceMesh = _referenceMeshConverter.Convert(skeletalMesh, export.GetPathName(), Math.Clamp(_selectedLodIndex, 0, lodCount - 1), AppendWorkflowLog);
            }
            catch (Exception ex)
            {
                AppendWorkflowLog($"Reference mesh conversion failed while preparing {export.GetPathName()}: {ex.Message}");
            }

            SkeletonPathTextBox.Text = $"[Auto] {export.GetPathName()} skeleton";
            SkeletonStatusText.Text = $"Auto-loaded destination skeleton from {export.GetPathName()}.";
            TryAutoLoadAnimSet(export.GetPathName(), skeletalMesh);
            DetailRows.Clear();
            DetailRows.Add(new RetargetDetailRow { Name = "Path", Value = export.GetPathName() });
            DetailRows.Add(new RetargetDetailRow { Name = "Selected LOD", Value = $"LOD {_selectedLodIndex}" });
            DetailRows.Add(new RetargetDetailRow { Name = "Bones", Value = skeletalMesh.RefSkeleton?.Count.ToString() ?? "0" });
            DetailRows.Add(new RetargetDetailRow { Name = "LOD Models", Value = skeletalMesh.LODModels?.Count.ToString() ?? "0" });
            DetailRows.Add(new RetargetDetailRow { Name = "Materials", Value = skeletalMesh.Materials?.Count.ToString() ?? "0" });
            DetailRows.Add(new RetargetDetailRow { Name = "Sockets", Value = skeletalMesh.Sockets?.Count.ToString() ?? "0" });
            DetailRows.Add(new RetargetDetailRow { Name = "Has Vertex Colors", Value = skeletalMesh.bHasVertexColors ? "Yes" : "No" });

            TargetMeshText.Text = $"{export.GetPathName()}  |  LODs: {skeletalMesh.LODModels?.Count ?? 0}  |  Selected: LOD {_selectedLodIndex}  |  Bones: {skeletalMesh.RefSkeleton?.Count ?? 0}";
            TargetMeshStatusText.Text = $"Selected target export: {export.GetPathName()}";
            RetargetSummaryText.Text = $"Reference mesh ready for retargeting: {export.GetPathName()}.";
            RetargetStatusText.Text = $"Selected {export.GetPathName()}.";
            RefreshMappingRows();
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            WorkflowStatusText.Text = _sourceMesh is null
                ? "Import a source mesh, then run map/scale/transfer."
                : "Reference and source are loaded. Use the workflow buttons below.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            RetargetStatusText.Text = $"Failed to inspect the selected UPK export while preparing the retarget target: {ex.Message}";
        }
    }

    private void TryAutoLoadAnimSet(string meshExportPath, USkeletalMesh skeletalMesh)
    {
        if (_currentHeader?.ExportTable is null)
            return;

        try
        {
            UAnimSet? foundAnimSet = DiscoverAnimSetForSkeletalMesh(meshExportPath, skeletalMesh);
            _animSet = foundAnimSet;

            if (foundAnimSet is not null)
            {
                string? animSetName = foundAnimSet.PreviewSkelMeshName?.Name;
                _animSetDisplayName = string.IsNullOrWhiteSpace(animSetName)
                    ? $"[Auto] {Path.GetFileNameWithoutExtension(meshExportPath)} AnimSet"
                    : $"[Auto] {animSetName}";
                AnimSetStatusText.Text = _animSetDisplayName;
                AnimationTransferRows.Clear();
                AnimationTransferStatusText.Text = $"AnimSet context loaded: {_animSetDisplayName}.";
                AppendWorkflowLog($"Auto-loaded AnimSet context with {foundAnimSet.TrackBoneNames?.Count ?? 0} track bone(s).");
            }
            else
            {
                _animSetDisplayName = string.Empty;
                AnimSetStatusText.Text = "[Auto] No AnimSet found";
                AnimationTransferRows.Clear();
                AnimationTransferStatusText.Text = "No AnimSet was discovered for transfer.";
                AppendWorkflowLog("No linked AnimSet was discovered automatically for the selected game SkeletalMesh.");
            }
        }
        catch (Exception ex)
        {
            _animSet = null;
            _animSetDisplayName = string.Empty;
            AnimSetStatusText.Text = "[Auto] AnimSet load failed";
            AnimationTransferRows.Clear();
            AnimationTransferStatusText.Text = $"AnimSet load failed while scanning {meshExportPath} for a linked animation set: {ex.Message}";
            AppendWorkflowLog($"Automatic AnimSet discovery failed while inspecting {meshExportPath} for a linked animation set: {ex.Message}");
        }
    }

    private UAnimSet? DiscoverAnimSetForSkeletalMesh(string meshExportPath, USkeletalMesh skeletalMesh)
    {
        if (_currentHeader?.ExportTable is null)
            return null;

        string meshObjectName = meshExportPath?.Split('.').LastOrDefault() ?? string.Empty;

        foreach (UnrealExportTableEntry export in _currentHeader.ExportTable)
        {
            string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
            if (!className.Equals(nameof(USkeletalMeshComponent), StringComparison.OrdinalIgnoreCase) &&
                !className.Equals("SkeletalMeshComponent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (export.UnrealObject is null)
                    export.ParseUnrealObject(false, false).GetAwaiter().GetResult();

                if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMeshComponent component)
                    continue;

                USkeletalMesh? referencedMesh = component.SkeletalMesh?.LoadObject<USkeletalMesh>();
                if (!ReferenceEquals(referencedMesh, skeletalMesh))
                    continue;

                foreach (FObject animSetRef in component.AnimSets ?? [])
                {
                    UAnimSet? animSet = animSetRef?.LoadObject<UAnimSet>();
                    if (animSet is not null)
                        return animSet;
                }
            }
            catch
            {
                continue;
            }
        }

        foreach (UnrealExportTableEntry export in _currentHeader.ExportTable)
        {
            string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
            if (!className.Equals(nameof(UAnimSet), StringComparison.OrdinalIgnoreCase) &&
                !className.Equals("AnimSet", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (export.UnrealObject is null)
                    export.ParseUnrealObject(false, false).GetAwaiter().GetResult();

                if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UAnimSet animSet)
                    continue;

                string? previewName = animSet.PreviewSkelMeshName?.Name;
                if (!string.IsNullOrWhiteSpace(previewName) &&
                    string.Equals(previewName, meshObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    return animSet;
                }

                if (animSet.TrackBoneNames is not null && animSet.TrackBoneNames.Count > 0)
                {
                    HashSet<string> trackNames = animSet.TrackBoneNames
                        .Where(static name => !string.IsNullOrWhiteSpace(name?.Name))
                        .Select(static name => name.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    int overlap = skeletalMesh.RefSkeleton.Count(bone => trackNames.Contains(bone.Name?.Name ?? string.Empty));
                    if (overlap >= Math.Max(4, skeletalMesh.RefSkeleton.Count / 4))
                        return animSet;
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private async Task SelectAndInspectPreferredExportAsync()
    {
        string? preferredExportPath = _launchContext?.ExportPath;
        RetargetExportViewModel? match = null;

        if (!string.IsNullOrWhiteSpace(preferredExportPath))
            match = ExportEntries.FirstOrDefault(entry =>
                string.Equals(entry.PathName, preferredExportPath, StringComparison.OrdinalIgnoreCase));

        match ??= ExportEntries.FirstOrDefault();
        if (match is null)
            return;

        _suppressExportSelectionInspect = true;
        ExportListView.SelectedItem = match;
        _suppressExportSelectionInspect = false;
        await InspectExportAsync(match).ConfigureAwait(true);
    }

    private async Task ImportSourceMeshAsync(string meshPath)
    {
        if (_busy)
            return;

        SetBusy(true, $"Importing {Path.GetFileName(meshPath)}...");
        SourceMeshStatusText.Text = $"Importing {Path.GetFileName(meshPath)}...";

        try
        {
            RetargetMeshImporter importer = new();
            ReportProgress(10, 100, "Reading mesh file.");
            _sourceMesh = await Task.Run(() => importer.Import(meshPath, AppendWorkflowLog)).ConfigureAwait(true);
            ReportProgress(70, 100, "Finalizing imported source mesh.");
            _boneMapping = null;
            _manualMappingOverrides.Clear();
            _processedMesh = null;
            SourceMeshStatusText.Text = $"Imported {Path.GetFileName(meshPath)}.";
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            if (_animSet is not null)
                _sourceMesh.AnimSet = _animSet;
            foreach (string path in _texturePaths)
                _sourceMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));
            RefreshMappingRows();
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            WorkflowStatusText.Text = _referenceMesh is null
                ? "Source mesh loaded. Select a reference UPK export next."
                : "Source mesh and reference are loaded. Use the workflow buttons below.";
            RetargetSummaryText.Text = _sourceMesh.Bones.Count > 0
                ? $"Source mesh loaded with {_sourceMesh.Bones.Count} bones."
                : "Source mesh loaded. This import appears unrigged.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
            ReportProgress(100, 100, "Source mesh imported.");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            SourceMeshStatusText.Text = $"Source mesh import failed while reading {Path.GetFileName(meshPath)} for the retarget source: {ex.Message}";
            SourceMeshSummaryText.Text = "No source mesh loaded.";
            AppendWorkflowLog($"Source mesh import failed while reading {Path.GetFileName(meshPath)} for the retarget source: {ex.Message}");
            SaveSessionState();
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task AutoMapBonesAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        try
        {
            _boneMapping = await Task.Run(() => _mappingService.AutoMap(_sourceMesh, targetSkeleton, _manualMappingOverrides, AppendWorkflowLog)).ConfigureAwait(true);
            WorkflowStatusText.Text = $"Auto-mapped {_boneMapping.Mapping.Count} bone(s).";
            RetargetSummaryText.Text = $"Auto-mapped {_boneMapping.Mapping.Count} bone(s) from the source mesh to the selected reference skeleton.";
            RefreshMappingRows();
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            UpdateRetargetContextSummary();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Auto map failed while matching the source mesh to the target skeleton: {ex.Message}";
            AppendWorkflowLog($"Auto map failed while matching the source mesh to the target skeleton: {ex.Message}");
        }
    }

    private async Task ValidateSkeletonAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            ValidationStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        try
        {
            ValidationResult result = await Task.Run(() => _validatorService.Validate(_sourceMesh, targetSkeleton, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
            ValidationRows.Clear();
            foreach (ValidationIssue issue in result.Issues)
            {
                ValidationRows.Add(new RetargetDetailRow
                {
                    Name = $"{issue.Severity}: {issue.Rule}",
                    Value = string.IsNullOrWhiteSpace(issue.BoneName)
                        ? issue.Message
                        : $"{issue.BoneName} - {issue.Message}"
                });
            }

            ValidationStatusText.Text = result.IsValid
                ? $"Validation passed with {result.WarningCount} warning(s)."
                : $"Validation found {result.ErrorCount} error(s) and {result.WarningCount} warning(s).";
            if (ValidationRows.Count == 0)
            {
                ValidationRows.Add(new RetargetDetailRow
                {
                    Name = "Validation",
                    Value = "No issues were reported."
                });
            }
        }
        catch (Exception ex)
        {
            ValidationStatusText.Text = $"Validation failed while checking the current retarget mapping: {ex.Message}";
            AppendWorkflowLog($"Validation failed while checking the current retarget mapping: {ex.Message}");
        }
    }

    private async Task TransferAnimSetMetadataAsync(bool includeNotifies, bool includeCurves)
    {
        await Task.Yield();
        UAnimSet? sourceAnimSet = GetAnimationTransferSource();
        if (sourceAnimSet is null)
        {
            AnimationTransferStatusText.Text = "Import or auto-load an AnimSet before transferring metadata.";
            return;
        }

        if (!includeNotifies && !includeCurves)
            return;

        try
        {
            UAnimSet? targetAnimSet = GetAnimationTransferTarget();
            AnimationTransferReport? notifyReport = null;
            AnimationTransferReport? curveReport = null;

            if (includeNotifies)
            {
                notifyReport = _notifyTransferService.Transfer(sourceAnimSet, targetAnimSet, AppendWorkflowLog);
                targetAnimSet = notifyReport.DestinationAnimSet;
            }

            if (includeCurves)
            {
                curveReport = _curveTransferService.Transfer(sourceAnimSet, targetAnimSet, AppendWorkflowLog);
                targetAnimSet = curveReport.DestinationAnimSet;
            }

            AnimationTransferReport report = includeNotifies && includeCurves
                ? AnimationTransferReflection.Merge("Animation Metadata Transfer", _animSetDisplayName ?? "AnimSet", targetAnimSet is null ? "Target" : targetAnimSet.PreviewSkelMeshName?.Name ?? targetAnimSet.GetType().Name, targetAnimSet, notifyReport!, curveReport!)
                : notifyReport ?? curveReport ?? throw new InvalidOperationException("No animation metadata transfer report was produced.");

            if (_processedMesh is not null)
                _processedMesh.AnimSet = report.DestinationAnimSet;
            else if (_sourceMesh is not null)
                _sourceMesh.AnimSet = report.DestinationAnimSet;

            _animSet = report.DestinationAnimSet;
            AnimationTransferRows.Clear();
            foreach (AnimationTransferEntry entry in report.Entries)
            {
                AnimationTransferRows.Add(new RetargetDetailRow
                {
                    Name = $"{entry.Status}: {entry.PropertyName}",
                    Value = string.IsNullOrWhiteSpace(entry.Details)
                        ? $"Items: {entry.ItemCount}"
                        : $"{entry.Details} (Items: {entry.ItemCount})"
                });
            }

            if (AnimationTransferRows.Count == 0)
            {
                AnimationTransferRows.Add(new RetargetDetailRow
                {
                    Name = "Animation transfer",
                    Value = "No notify or curve properties were found."
                });
            }

            string mode = includeNotifies && includeCurves
                ? "notifies and curves"
                : includeNotifies
                    ? "notifies"
                    : "curves";

            AnimationTransferStatusText.Text = $"{report.TransferKind} copied {report.CopiedCount} property set(s) from {_animSetDisplayName ?? "AnimSet"}.";
            WorkflowStatusText.Text = $"Transferred {mode} into the current retarget AnimSet.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            AnimationTransferStatusText.Text = $"Animation metadata transfer failed while copying notifies or curves: {ex.Message}";
            AppendWorkflowLog($"Animation metadata transfer failed while copying notifies or curves: {ex.Message}");
        }
    }

    private async Task SelectBatchFolderAsync()
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        _batchFolderPath = folder.Path;
        BatchFolderTextBox.Text = folder.Path;
        BatchRetargetStatusText.Text = $"Batch folder selected: {folder.Path}";
        SaveSessionState();
    }

    private void ApplySessionState()
    {
        WorkspaceSessionStore.RetargetWorkspaceSession session = WorkspaceSessionStore.Retarget;
        if (!string.IsNullOrWhiteSpace(session.UpkPath))
            UpkPathTextBox.Text = session.UpkPath;
        if (!string.IsNullOrWhiteSpace(session.ExportPath))
            _selectedExportPath = session.ExportPath;
        if (!string.IsNullOrWhiteSpace(session.BatchFolderPath))
            _batchFolderPath = session.BatchFolderPath;
        if (!string.IsNullOrWhiteSpace(session.SelectedPosePreset) &&
            Enum.TryParse(session.SelectedPosePreset, out RetargetPosePreset preset))
        {
            _selectedPosePreset = preset;
        }
    }

    private void SaveSessionState()
    {
        if (_suppressSessionWrites)
            return;

        WorkspaceSessionStore.RememberRetarget(new WorkspaceSessionStore.RetargetWorkspaceSession
        {
            UpkPath = _currentUpkPath ?? string.Empty,
            ExportPath = _selectedExportPath ?? string.Empty,
            BatchFolderPath = _batchFolderPath ?? string.Empty,
            SelectedPosePreset = _selectedPosePreset.ToString()
        });
    }

    private void TryRestoreSession()
    {
        WorkspaceSessionStore.RetargetWorkspaceSession session = WorkspaceSessionStore.Retarget;
        if (!string.IsNullOrWhiteSpace(session.UpkPath) && File.Exists(session.UpkPath) && string.IsNullOrWhiteSpace(_currentUpkPath))
        {
            UpkPathTextBox.Text = session.UpkPath;
            _ = LoadUpkAsync(session.UpkPath);
        }
    }

    private async Task BatchRetargetAsync()
    {
        RetargetMesh? outputMesh = _processedMesh ?? _sourceMesh;
        if (outputMesh is null || _currentHeader is null || string.IsNullOrWhiteSpace(_selectedExportPath))
        {
            BatchRetargetStatusText.Text = "Load a source mesh and select a SkeletalMesh export before running batch retarget.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_batchFolderPath) || !Directory.Exists(_batchFolderPath))
        {
            BatchRetargetStatusText.Text = "Select a valid batch folder first.";
            return;
        }

        try
        {
            SetBusy(true, "Running batch retarget...");
            BatchRetargetRows.Clear();
            BatchRetargetStatusText.Text = $"Retargeting UPKs under {_batchFolderPath}...";
            bool replaceAllLods = ReplaceAllLodsCheckBox.IsChecked == true;

            Progress<BatchRetargetProgress> progress = new(progressState =>
            {
                if (!string.IsNullOrWhiteSpace(progressState.Status))
                    BatchRetargetStatusText.Text = progressState.Status;
                double maximum = Math.Max(1, progressState.Total);
                ReportProgress(progressState.Processed, maximum, progressState.Status);
            });

            BatchRetargetReport report = await _batchRetargetService.RetargetFolderAsync(
                _batchFolderPath,
                _selectedExportPath,
                outputMesh,
                _selectedLodIndex,
                replaceAllLods,
                GetRetargetBatchLogDirectory(),
                AppendWorkflowLog,
                progress).ConfigureAwait(true);

            foreach (BatchRetargetEntry entry in report.Entries)
            {
                BatchRetargetRows.Add(new RetargetDetailRow
                {
                    Name = $"{entry.Status}: {Path.GetFileName(entry.FilePath)}",
                    Value = entry.Status.Equals("Success", StringComparison.OrdinalIgnoreCase)
                        ? $"Backup: {entry.BackupPath}"
                        : entry.Error
                });
            }

            BatchRetargetStatusText.Text = $"Batch retarget completed: {report.SuccessCount} succeeded, {report.FailureCount} failed.";
            WorkflowStatusText.Text = BatchRetargetStatusText.Text;
            BatchRetargetLogText.Text = $"Batch log written to {report.LogPath}";
            AppendWorkflowLog($"Batch retarget log written to {report.LogPath}");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            BatchRetargetStatusText.Text = $"Batch retarget failed while processing the selected folder: {ex.Message}";
            BatchRetargetLogText.Text = "Batch log was not written because the batch run failed.";
            AppendWorkflowLog($"Batch retarget failed while processing the selected folder: {ex.Message}");
            SaveSessionState();
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private UAnimSet? GetAnimationTransferSource()
    {
        if (_animSet is not null)
            return _animSet;

        if (_processedMesh?.AnimSet is not null)
            return _processedMesh.AnimSet;

        if (_sourceMesh?.AnimSet is not null)
            return _sourceMesh.AnimSet;

        return _referenceMesh?.AnimSet;
    }

    private UAnimSet? GetAnimationTransferTarget()
    {
        if (_processedMesh?.AnimSet is not null)
            return _processedMesh.AnimSet;

        if (_sourceMesh?.AnimSet is not null)
            return _sourceMesh.AnimSet;

        return _referenceMesh?.AnimSet;
    }

    private static string GetRetargetBatchLogDirectory()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktop, "OmegaAssetStudio_RuntimeLogs", "Retargeter");
    }

    private async Task SaveMappingProfileAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            MappingProfileStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        if (_boneMapping is null || _boneMapping.Mapping.Count == 0)
        {
            MappingProfileStatusText.Text = "Run Auto Map Bones or apply a manual override before saving a profile.";
            return;
        }

        string profileName = string.IsNullOrWhiteSpace(MappingProfileNameTextBox.Text)
            ? Path.GetFileNameWithoutExtension(_sourceMesh.SourcePath)
            : MappingProfileNameTextBox.Text.Trim();

        try
        {
            await Task.Run(() => _mappingService.SaveProfile(profileName, _sourceMesh, targetSkeleton, _boneMapping, _manualMappingOverrides, AppendWorkflowLog)).ConfigureAwait(true);
            MappingProfileStatusText.Text = $"Saved profile '{profileName}' to {_mappingService.GetProfilePath(profileName)}";
        }
        catch (Exception ex)
        {
            MappingProfileStatusText.Text = $"Profile save failed while writing the mapping profile: {ex.Message}";
            AppendWorkflowLog($"Profile save failed while writing the mapping profile: {ex.Message}");
        }
    }

    private async Task LoadMappingProfileAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            MappingProfileStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        string profileName = string.IsNullOrWhiteSpace(MappingProfileNameTextBox.Text)
            ? Path.GetFileNameWithoutExtension(_sourceMesh.SourcePath)
            : MappingProfileNameTextBox.Text.Trim();

        try
        {
            if (!_mappingService.TryLoadProfile(profileName, out RetargetMappingProfile? profile, out string path, out string error) || profile is null)
            {
                MappingProfileStatusText.Text = error;
                return;
            }

            _boneMapping = await Task.Run(() => _mappingService.ApplyProfile(_sourceMesh, targetSkeleton, profile, AppendWorkflowLog)).ConfigureAwait(true);
            _manualMappingOverrides.Clear();
            foreach ((string source, string target) in profile.ManualOverrides)
                _manualMappingOverrides[source] = target;

            MappingProfileNameTextBox.Text = profile.ProfileName;
            MappingProfileStatusText.Text = $"Loaded profile '{profile.ProfileName}' from {path}.";
            WorkflowStatusText.Text = $"Loaded mapping profile '{profile.ProfileName}'.";
            RefreshMappingRows();
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            UpdateRetargetContextSummary();
        }
        catch (Exception ex)
        {
            MappingProfileStatusText.Text = $"Profile load failed while reading the mapping profile: {ex.Message}";
            AppendWorkflowLog($"Profile load failed while reading the mapping profile: {ex.Message}");
        }
    }

    private async Task ApplyManualOverrideAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            MappingProfileStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        string sourceBone = ManualOverrideSourceTextBox.Text?.Trim() ?? string.Empty;
        string targetBone = ManualOverrideTargetTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceBone) || string.IsNullOrWhiteSpace(targetBone))
        {
            MappingProfileStatusText.Text = "Enter both a source bone and a target bone.";
            return;
        }

        try
        {
            _boneMapping ??= new BoneMappingResult();
            await Task.Run(() => _mappingService.ApplyManualOverride(_sourceMesh, _boneMapping, sourceBone, targetBone, AppendWorkflowLog)).ConfigureAwait(true);
            _manualMappingOverrides[sourceBone] = targetBone;
            MappingProfileStatusText.Text = $"Applied manual override {sourceBone} -> {targetBone}.";
            WorkflowStatusText.Text = "Manual mapping override applied.";
            RefreshMappingRows();
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            UpdateRetargetContextSummary();
        }
        catch (Exception ex)
        {
            MappingProfileStatusText.Text = $"Manual override failed while applying the source-to-target mapping: {ex.Message}";
            AppendWorkflowLog($"Manual override failed while applying the source-to-target mapping: {ex.Message}");
        }
    }

    private async Task AutoOrientSourceAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        if (_referenceMesh is null && (_boneMapping is null || _boneMapping.Mapping.Count == 0))
        {
            WorkflowStatusText.Text = "Auto orientation needs either a mapped source or a converted reference mesh.";
            return;
        }

        try
        {
            OrientationProcessor processor = new();
            RetargetMesh rotated = await Task.Run(() =>
            {
                Quaternion rotation = (_boneMapping is { Mapping.Count: > 0 } && _sourceMesh.Bones.Count > 0)
                    ? processor.ComputeAlignmentRotation(_sourceMesh, targetSkeleton, _boneMapping.Mapping, AppendWorkflowLog)
                    : _referenceMesh is not null
                        ? processor.ComputeGeometryAlignmentRotation(_sourceMesh, _referenceMesh, targetSkeleton, AppendWorkflowLog)
                        : Quaternion.Identity;

                return processor.ApplyRotation(_sourceMesh, rotation, AppendWorkflowLog);
            }).ConfigureAwait(true);

            _sourceMesh = rotated;
            _processedMesh = null;
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = "Auto orientation applied to the source mesh.";
            RetargetSummaryText.Text = "Auto orientation completed. You can now auto scale or transfer weights.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Auto orientation failed while normalizing the source mesh: {ex.Message}";
            AppendWorkflowLog($"Auto orientation failed while normalizing the source mesh: {ex.Message}");
        }
    }

    private async Task AutoScaleSourceAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        try
        {
            AutoScaleProcessor processor = new();
            float scaleFactor = await Task.Run(() => processor.ComputeScaleFactor(_sourceMesh, targetSkeleton, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
            _sourceMesh = await Task.Run(() => processor.ApplyScale(_sourceMesh, scaleFactor, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = null;
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count} | Scale: {_sourceMesh.AppliedScale:0.###}x";
            WorkflowStatusText.Text = $"Auto scale applied: {scaleFactor:0.###}x.";
            RetargetSummaryText.Text = $"Source mesh scaled to match the selected reference skeleton.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Auto scale failed while fitting the source mesh for UE3 export: {ex.Message}";
            AppendWorkflowLog($"Auto scale failed while fitting the source mesh for UE3 export: {ex.Message}");
        }
    }

    private async Task AutoAlignPoseAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        try
        {
            IReadOnlyList<PoseAdjustment> adjustments = await Task.Run(() => _poseAlignmentService.Analyze(_sourceMesh, targetSkeleton, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
            if (adjustments.Count == 0)
            {
                WorkflowStatusText.Text = "Pose alignment found no corrective step to apply.";
                RetargetSummaryText.Text = "The current source mesh already matches the target pose closely.";
                return;
            }

            _sourceMesh = await Task.Run(() => _poseAlignmentService.Apply(_sourceMesh, targetSkeleton, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = null;
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = $"Auto pose alignment applied with {adjustments.Count} corrective step(s).";
            RetargetSummaryText.Text = "Auto pose alignment adjusted relaxed bind-pose bones before retargeting.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Auto pose alignment failed while fitting the source mesh to the target: {ex.Message}";
            AppendWorkflowLog($"Auto pose alignment failed while fitting the source mesh to the target: {ex.Message}");
        }
    }

    private async Task AutoFixMirrorAsync()
    {
        if (_sourceMesh is null)
        {
            WorkflowStatusText.Text = "Load a source mesh first.";
            return;
        }

        try
        {
            MirrorFixAnalysis analysis = await Task.Run(() => _mirrorFixService.Analyze(_sourceMesh, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
            MirrorFixRows.Clear();
            MirrorFixRows.Add(new RetargetDetailRow { Name = "Mirror Detection", Value = analysis.ShouldApply ? "Mirrored" : "Not mirrored" });
            MirrorFixRows.Add(new RetargetDetailRow { Name = "Axis", Value = analysis.Axis });
            MirrorFixRows.Add(new RetargetDetailRow { Name = "Determinant", Value = analysis.Determinant.ToString("0.###") });
            MirrorFixRows.Add(new RetargetDetailRow { Name = "Reason", Value = analysis.Reason });
            foreach (string note in analysis.Notes)
                MirrorFixRows.Add(new RetargetDetailRow { Name = "Note", Value = note });

            MirrorFixStatusText.Text = analysis.ShouldApply
                ? "Mirrored handedness detected. Applying reflection fix."
                : "No mirrored handedness detected.";

            if (!analysis.ShouldApply)
            {
                WorkflowStatusText.Text = "Mirror check completed without changes.";
                RetargetSummaryText.Text = "The source mesh did not need a mirror correction.";
                return;
            }

            _sourceMesh = await Task.Run(() => _mirrorFixService.Apply(_sourceMesh, analysis, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = null;
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = "Mirror fix applied to the source mesh.";
            RetargetSummaryText.Text = "Mirrored handedness corrected before retargeting.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Mirror fix failed while correcting mesh handedness: {ex.Message}";
            AppendWorkflowLog($"Mirror fix failed while correcting mesh handedness: {ex.Message}");
        }
    }

    private async Task SolveTransformsAsync()
    {
        if (_sourceMesh is null)
        {
            WorkflowStatusText.Text = "Load a source mesh first.";
            return;
        }

        try
        {
            TransformSolveReport report = await Task.Run(() => _transformSolverService.Analyze(_sourceMesh, AppendWorkflowLog)).ConfigureAwait(true);
            TransformSolverRows.Clear();
            TransformSolverRows.Add(new RetargetDetailRow { Name = "Bones Fixed", Value = report.BonesFixed.ToString() });
            TransformSolverRows.Add(new RetargetDetailRow { Name = "Vertices Fixed", Value = report.VerticesFixed.ToString() });
            TransformSolverRows.Add(new RetargetDetailRow { Name = "Invalid Transforms", Value = report.InvalidTransforms.ToString() });
            foreach (string note in report.Notes)
                TransformSolverRows.Add(new RetargetDetailRow { Name = "Note", Value = note });

            TransformSolverStatusText.Text = report.BonesFixed > 0 || report.VerticesFixed > 0 || report.InvalidTransforms > 0
                ? "Transform cleanup needed. Applying solver."
                : "Transforms already look stable.";

            if (report.BonesFixed == 0 && report.VerticesFixed == 0 && report.InvalidTransforms == 0)
            {
                WorkflowStatusText.Text = "Transform solver completed without changes.";
                RetargetSummaryText.Text = "The source mesh transforms were already stable.";
                return;
            }

            _sourceMesh = await Task.Run(() => _transformSolverService.Apply(_sourceMesh, report, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = null;
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = "Transform solver applied.";
            RetargetSummaryText.Text = "Unstable transforms were normalized before retargeting.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Transform solver failed while stabilizing the source transforms: {ex.Message}";
            AppendWorkflowLog($"Transform solver failed while stabilizing the source transforms: {ex.Message}");
        }
    }

    private async Task TransferWeightsAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || _referenceMesh is null || targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        try
        {
            WeightTransferEngine transfer = new();
            _sourceMesh = await Task.Run(() => transfer.TransferWeights(_referenceMesh, _sourceMesh, targetSkeleton.Bones, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = _sourceMesh;
            if (_animSet is not null)
                _processedMesh.AnimSet = _animSet;
            foreach (string path in _texturePaths)
                _processedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = "Weights transferred to the imported source mesh.";
            RetargetSummaryText.Text = $"Transferred weights using the selected reference mesh as the original MHO basis.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Weight transfer failed while baking retarget weights: {ex.Message}";
            AppendWorkflowLog($"Weight transfer failed while baking retarget weights: {ex.Message}");
        }
    }

    private async Task OneClickRetargetAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (_sourceMesh is null || _referenceMesh is null || targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Load a source mesh and select a reference UPK export first.";
            return;
        }

        try
        {
            if (_sourceMesh.Bones.Count > 0 && (_boneMapping is null || _boneMapping.Mapping.Count == 0))
            {
                _boneMapping = await Task.Run(() => _mappingService.AutoMap(_sourceMesh, targetSkeleton, _manualMappingOverrides, AppendWorkflowLog)).ConfigureAwait(true);
                RefreshMappingRows();
            }

            if (_sourceMesh.Bones.Count > 0)
            {
                MirrorFixAnalysis mirrorAnalysis = await Task.Run(() => _mirrorFixService.Analyze(_sourceMesh, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
                if (mirrorAnalysis.ShouldApply)
                    _sourceMesh = await Task.Run(() => _mirrorFixService.Apply(_sourceMesh, mirrorAnalysis, AppendWorkflowLog)).ConfigureAwait(true);

                OrientationProcessor orientation = new();
                Quaternion rotation = _boneMapping is { Mapping.Count: > 0 }
                    ? orientation.ComputeAlignmentRotation(_sourceMesh, targetSkeleton, _boneMapping.Mapping, AppendWorkflowLog)
                    : Quaternion.Identity;
                _sourceMesh = await Task.Run(() => orientation.ApplyRotation(_sourceMesh, rotation, AppendWorkflowLog)).ConfigureAwait(true);
            }

            AutoScaleProcessor scaler = new();
            float scaleFactor = await Task.Run(() => scaler.ComputeScaleFactor(_sourceMesh, targetSkeleton, _boneMapping?.Mapping, AppendWorkflowLog)).ConfigureAwait(true);
            _sourceMesh = await Task.Run(() => scaler.ApplyScale(_sourceMesh, scaleFactor, AppendWorkflowLog)).ConfigureAwait(true);

            TransformSolveReport transformReport = await Task.Run(() => _transformSolverService.Analyze(_sourceMesh, AppendWorkflowLog)).ConfigureAwait(true);
            if (transformReport.BonesFixed > 0 || transformReport.VerticesFixed > 0 || transformReport.InvalidTransforms > 0)
                _sourceMesh = await Task.Run(() => _transformSolverService.Apply(_sourceMesh, transformReport, AppendWorkflowLog)).ConfigureAwait(true);

            WeightTransferEngine transfer = new();
            _sourceMesh = await Task.Run(() => transfer.TransferWeights(_referenceMesh, _sourceMesh, targetSkeleton.Bones, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = _sourceMesh;
            if (_animSet is not null)
                _processedMesh.AnimSet = _animSet;
            foreach (string path in _texturePaths)
                _processedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));

            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = $"One-click retarget completed. Scale: {scaleFactor:0.###}x.";
            RetargetSummaryText.Text = "One-click retarget completed and weights were transferred.";
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"One-click retarget failed while building the final retargeted mesh: {ex.Message}";
            AppendWorkflowLog($"One-click retarget failed while building the final retargeted mesh: {ex.Message}");
        }
    }

    private async Task ImportRetargetSkeletonAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".fbx");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        if (_busy)
            return;

        SetBusy(true, $"Importing {Path.GetFileName(file.Path)}...");
        SkeletonStatusText.Text = $"Importing {Path.GetFileName(file.Path)}...";

        try
        {
            SkeletonImporter importer = new();
            ReportProgress(15, 100, "Reading skeleton file.");
            _playerSkeleton = await Task.Run(() => importer.Import(file.Path, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = null;
            SkeletonPathTextBox.Text = file.Path;
            SkeletonStatusText.Text = $"Imported {_playerSkeleton.Bones.Count} player skeleton bones.";
            _boneMapping = null;
            _manualMappingOverrides.Clear();
            RefreshMappingRows();
            ValidationRows.Clear();
            ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
            WorkflowStatusText.Text = _sourceMesh is null
                ? "Player skeleton loaded. Import a source mesh next."
                : "Player skeleton and source mesh are ready for retargeting.";
            AppendWorkflowLog($"Imported player skeleton from {file.Path}.");
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
            ReportProgress(100, 100, "Player skeleton imported.");
        }
        catch (Exception ex)
        {
            SkeletonStatusText.Text = $"Skeleton import failed while reading {Path.GetFileName(file.Path)} for retargeting: {ex.Message}";
            AppendWorkflowLog($"Skeleton import failed while reading {Path.GetFileName(file.Path)} for retargeting: {ex.Message}");
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task ImportRetargetAnimSetAsync()
    {
        if (_currentHeader is null && string.IsNullOrWhiteSpace(_currentUpkPath))
        {
            WorkflowStatusText.Text = "Load a reference UPK first.";
            return;
        }

        if (_currentHeader is null && !string.IsNullOrWhiteSpace(_currentUpkPath))
            await LoadUpkAsync(_currentUpkPath).ConfigureAwait(true);

        if (_currentHeader is null)
            return;

        if (_busy)
            return;

        SetBusy(true, "Importing AnimSet...");
        AnimSetStatusText.Text = "Importing AnimSet...";

        try
        {
            ReportProgress(20, 100, "Scanning AnimSet exports.");
            UnrealExportTableEntry? export = null;
            foreach (UnrealExportTableEntry candidate in _currentHeader.ExportTable)
            {
                string className = candidate.ClassReferenceNameIndex?.Name ?? string.Empty;
                if (className.Equals(nameof(UAnimSet), StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("AnimSet", StringComparison.OrdinalIgnoreCase))
                {
                    export = candidate;
                    break;
                }
            }

            if (export is null)
                throw new InvalidOperationException("The selected UPK does not contain an AnimSet export.");

            if (export.UnrealObject is null)
                await export.ParseUnrealObject(false, false).ConfigureAwait(true);

            if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UAnimSet animSet)
                throw new InvalidOperationException("The selected export is not an AnimSet.");

            _animSet = animSet;
            _animSetDisplayName = export.GetPathName();
            if (_sourceMesh is not null)
                _sourceMesh.AnimSet = animSet;
            if (_processedMesh is not null)
                _processedMesh.AnimSet = animSet;

            AnimSetStatusText.Text = $"Imported AnimSet: {export.GetPathName()}";
            AnimationTransferRows.Clear();
            AnimationTransferStatusText.Text = $"Imported AnimSet context: {export.GetPathName()}";
            AppendWorkflowLog($"Imported AnimSet '{export.GetPathName()}' with {animSet.TrackBoneNames?.Count ?? 0} track bone(s).");
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
            ReportProgress(100, 100, "AnimSet imported.");
        }
        catch (Exception ex)
        {
            string animSetLabel = string.IsNullOrWhiteSpace(_currentUpkPath)
                ? "the current UPK"
                : Path.GetFileName(_currentUpkPath);
            AnimSetStatusText.Text = $"AnimSet import failed while reading {animSetLabel} for the current retarget session: {ex.Message}";
            AnimationTransferRows.Clear();
            AnimationTransferStatusText.Text = $"AnimSet import failed while reading {animSetLabel} for the current retarget session: {ex.Message}";
            AppendWorkflowLog($"AnimSet import failed while reading {animSetLabel} for the current retarget session: {ex.Message}");
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task ImportRetargetTexturesAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".dds");
        picker.FileTypeFilter.Add(".tga");
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
            return;

        SetBusy(true, "Importing textures...");
        ReportProgress(10, 100, "Staging texture files.");
        _texturePaths.Clear();
        foreach (var file in files)
            _texturePaths.Add(file.Path);

        TexturesStatusText.Text = $"{_texturePaths.Count} texture file(s) loaded.";
        AppendWorkflowLog($"Imported {_texturePaths.Count} texture file(s).");

        if (_sourceMesh is not null)
        {
            _sourceMesh.Textures.Clear();
            foreach (string path in _texturePaths)
                _sourceMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));
        }

        if (_processedMesh is not null)
        {
            _processedMesh.Textures.Clear();
            foreach (string path in _texturePaths)
                _processedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));
        }

        UpdateRetargetContextSummary();
        QueueRetargetPreviewRefresh();
        ReportProgress(100, 100, "Textures imported.");
        SetBusy(false, "Ready.");
    }

    private async Task ApplyUe3FixesAsync()
    {
        SkeletonDefinition? targetSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (targetSkeleton is null)
        {
            WorkflowStatusText.Text = "Import a player skeleton first.";
            return;
        }

        if (_processedMesh is null && _sourceMesh is null)
        {
            WorkflowStatusText.Text = "Import or retarget a mesh first.";
            return;
        }

        try
        {
            SetBusy(true, "Applying UE3 compatibility fixes...");
            ReportProgress(20, 100, "Preparing compatibility pass.");
            UE3CompatibilityProcessor processor = new();
            RetargetMesh input = _processedMesh ?? _sourceMesh!;
            IReadOnlyDictionary<string, string> mapping = _boneMapping?.Mapping ?? new Dictionary<string, string>();
            ReportProgress(60, 100, "Rebuilding mesh for UE3.");
            RetargetMesh compatible = await Task.Run(() => processor.Process(input, targetSkeleton, mapping, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = compatible;
            if (_animSet is not null)
                _processedMesh.AnimSet = _animSet;
            foreach (string path in _texturePaths)
                _processedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));

            CompatibilityStatusText.Text = $"UE3 compatibility fixes applied. UV sets: {_processedMesh.MaxUvSets}, bones: {_processedMesh.Bones.Count}.";
            OutputStatusText.Text = "Compatibility-ready mesh prepared for export.";
            AppendWorkflowLog($"UE3 compatibility fixes applied to {_processedMesh.MeshName}.");
            UpdateRetargetContextSummary();
            QueueRetargetPreviewRefresh();
            ReportProgress(100, 100, "UE3 compatibility complete.");
        }
        catch (Exception ex)
        {
            CompatibilityStatusText.Text = $"UE3 compatibility prep failed while normalizing the source mesh for export: {ex.Message}";
            AppendWorkflowLog($"UE3 compatibility prep failed while normalizing the source mesh for export: {ex.Message}");
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task ExportRetargetFbxAsync()
    {
        RetargetMesh? outputMesh = _processedMesh ?? _sourceMesh;
        if (outputMesh is null)
        {
            OutputStatusText.Text = "Retarget a mesh first.";
            return;
        }

        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("FBX", [".fbx"]);
        picker.SuggestedFileName = $"{Path.GetFileNameWithoutExtension(outputMesh.MeshName)}_retargeted";

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        SetBusy(true, "Exporting FBX...");
        ReportProgress(20, 100, "Writing FBX file.");
        FBX2013Exporter exporter = new();
        try
        {
            await Task.Run(() => exporter.Export(file.Path, outputMesh, AppendWorkflowLog)).ConfigureAwait(true);
            OutputStatusText.Text = $"Exported FBX to {file.Path}.";
            ReportProgress(100, 100, "FBX export complete.");
        }
        catch (Exception ex)
        {
            OutputStatusText.Text = $"FBX export failed while writing {file.Path} for the retarget output: {ex.Message}";
            AppendWorkflowLog($"FBX export failed while writing {file.Path} for the retarget output: {ex.Message}");
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task ReplaceRetargetMeshAsync()
    {
        RetargetMesh? outputMesh = _processedMesh ?? _sourceMesh;
        if (outputMesh is null)
        {
            OutputStatusText.Text = "Retarget a mesh first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentUpkPath) || ExportListView.SelectedItem is not RetargetExportViewModel selected)
        {
            OutputStatusText.Text = "Select a reference UPK export first.";
            return;
        }

        try
        {
            SetBusy(true, "Replacing mesh in UPK...");
            ReportProgress(10, 100, "Preparing replacement build.");
            MeshReplacer replacer = new();
            string backupPath = await replacer.ReplaceMeshInUpkAsync(
                _currentUpkPath,
                selected.PathName,
                outputMesh,
                Math.Max(0, _selectedLodIndex),
                replaceAllLods: false,
                AppendWorkflowLog).ConfigureAwait(true);

            OutputStatusText.Text = $"Replacement completed. Backup: {backupPath}";
            AppendWorkflowLog($"Replaced mesh in {_currentUpkPath}. Backup: {backupPath}");
            UpdateRetargetContextSummary();
            ReportProgress(100, 100, "UPK replacement complete.");
        }
        catch (Exception ex)
        {
            OutputStatusText.Text = $"Replacement failed while applying the selected export to the current UPK: {ex.Message}";
            AppendWorkflowLog($"Replacement failed while applying the selected export to the current UPK: {ex.Message}");
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task ApplyPosePreviewAsync()
    {
        RetargetMesh? previewSource = GetRetargetPosePreviewSourceMesh();
        if (previewSource is null)
        {
            PosePreviewStatusText.Text = "Load a source mesh or retargeted mesh first.";
            return;
        }

        try
        {
            RetargetMesh previewMesh = await Task.Run(() => _posePreviewService.ApplyPose(previewSource, _selectedPosePreset, AppendWorkflowLog)).ConfigureAwait(true);
            _processedMesh = previewMesh;
            if (_animSet is not null)
                _processedMesh.AnimSet = _animSet;
            foreach (string path in _texturePaths)
                _processedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));

            PosePreviewStatusText.Text = $"Showing {_selectedPosePreset} on {_processedMesh.MeshName}.";
            WorkflowStatusText.Text = "Pose preview applied.";
            AppendWorkflowLog($"Pose preview applied: {_selectedPosePreset}.");
            await RefreshRetargetPreviewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PosePreviewStatusText.Text = $"Pose preview failed while applying the requested pose to the current preview mesh: {ex.Message}";
            AppendWorkflowLog($"Pose preview failed while applying the requested pose to the current preview mesh: {ex.Message}");
        }
    }

    private async Task ResetPosePreviewAsync()
    {
        RetargetMesh? previewSource = GetRetargetPosePreviewSourceMesh();
        if (previewSource is null)
        {
            PosePreviewStatusText.Text = "Load a source mesh or retargeted mesh first.";
            return;
        }

        await Task.Yield();
        _processedMesh = previewSource;
        if (_animSet is not null)
            _processedMesh.AnimSet = _animSet;
        foreach (string path in _texturePaths)
            _processedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));

        PosePreviewStatusText.Text = $"Showing bind pose on {_processedMesh.MeshName}.";
        WorkflowStatusText.Text = "Pose preview reset.";
        AppendWorkflowLog("Pose preview reset to bind pose.");
        await RefreshRetargetPreviewAsync().ConfigureAwait(true);
    }

    private async Task ResetRetargetPreviewCameraAsync()
    {
        RetargetMesh? previewSource = GetRetargetPosePreviewSourceMesh();
        if (previewSource is null)
        {
            RetargetPreviewStatusText.Text = "Load a source mesh or retargeted mesh first.";
            return;
        }

        if (_retargetPreviewMesh is null)
            await RefreshRetargetPreviewAsync().ConfigureAwait(true);
        else
        {
            _retargetPreviewCamera.Reset(_retargetPreviewMesh.Center, MathF.Max(1.0f, _retargetPreviewMesh.Radius));
            await RenderRetargetPreviewAsync().ConfigureAwait(true);
        }
    }

    private void InitializeRetargetPreview()
    {
        _retargetPreviewScene.DisplayMode = MeshPreviewDisplayMode.FbxOnly;
        _retargetPreviewScene.ShowFbxMesh = true;
        _retargetPreviewScene.ShowUe3Mesh = false;
        _retargetPreviewScene.Wireframe = false;
        _retargetPreviewScene.ShowBones = false;
        _retargetPreviewScene.ShowWeights = false;
        _retargetPreviewScene.ShowSections = false;
        _retargetPreviewScene.ShowNormals = false;
        _retargetPreviewScene.ShowTangents = false;
        _retargetPreviewScene.ShowUvSeams = false;
        _retargetPreviewScene.MaterialPreviewEnabled = false;
        _retargetPreviewScene.ShadingMode = MeshPreviewShadingMode.Lit;
        _retargetPreviewScene.BackgroundStyle = MeshPreviewBackgroundStyle.DarkGradient;
        _retargetPreviewScene.LightingPreset = MeshPreviewLightingPreset.Neutral;
        _retargetPreviewScene.ShowGroundPlane = true;
        RetargetPreviewStatsText.Text = "Idle";
        RetargetPreviewStatusText.Text = "Pose preview will appear here once a source mesh or retarget mesh is loaded.";
    }

    private async Task RefreshRetargetPreviewAsync()
    {
        if (_retargetPreviewRenderInProgress)
        {
            _retargetPreviewRenderPending = true;
            return;
        }

        _retargetPreviewRenderInProgress = true;
        try
        {
            do
            {
                _retargetPreviewRenderPending = false;
                RetargetMesh? previewSource = GetRetargetPosePreviewSourceMesh();
                if (previewSource is null)
                {
                    ClearRetargetPreview();
                    return;
                }

                RetargetPreviewStatusText.Text = _processedMesh is not null
                    ? $"Previewing {_selectedPosePreset} on {_processedMesh.MeshName}."
                    : $"Previewing {previewSource.MeshName}.";

                MeshPreviewMesh previewMesh = await Task.Run(() => _retargetPreviewConverter.Convert(previewSource, previewSource.MeshName, AppendWorkflowLog)).ConfigureAwait(true);
                _retargetPreviewMesh = previewMesh;
                _retargetPreviewScene.SetFbxMesh(previewMesh);
                _retargetPreviewScene.SetUe3Mesh(null);
                _retargetPreviewCamera.Reset(previewMesh.Center, MathF.Max(1.0f, previewMesh.Radius));

                await RenderRetargetPreviewAsync().ConfigureAwait(true);
                RetargetPreviewStatsText.Text = BuildRetargetPreviewStatsText();
            }
            while (_retargetPreviewRenderPending);
        }
        catch (Exception ex)
        {
            RetargetPreviewStatusText.Text = $"Pose preview render failed while drawing the current source mesh in the viewport: {ex.Message}";
            AppendWorkflowLog($"Pose preview render failed while drawing the current source mesh in the viewport: {ex.Message}");
            ClearRetargetPreview();
        }
        finally
        {
            _retargetPreviewRenderInProgress = false;
        }
    }

    private async Task RenderRetargetPreviewAsync()
    {
        if (_retargetPreviewMesh is null)
            return;

        int width = (int)Math.Max(320, RetargetPreviewSwapChainPanel.ActualWidth > 0 ? RetargetPreviewSwapChainPanel.ActualWidth : (RetargetPreviewImage.ActualWidth > 0 ? RetargetPreviewImage.ActualWidth : 960));
        int height = (int)Math.Max(240, RetargetPreviewSwapChainPanel.ActualHeight > 0 ? RetargetPreviewSwapChainPanel.ActualHeight : (RetargetPreviewImage.ActualHeight > 0 ? RetargetPreviewImage.ActualHeight : 540));

        if (_retargetPreviewSurfaceLoaded && RetargetPreviewSwapChainPanel.XamlRoot is not null)
        {
            RetargetPreviewImage.Source = null;
            RetargetPreviewImage.Visibility = Visibility.Collapsed;
            RetargetPreviewSwapChainPanel.Visibility = Visibility.Visible;
            _retargetPreviewRenderer.AttachToPanel(RetargetPreviewSwapChainPanel, DispatcherQueue);
            _retargetPreviewRenderer.SetFrame(_retargetPreviewScene, _retargetPreviewCamera);
            if (!_retargetPreviewRenderer.LastRenderSucceeded)
            {
                RetargetPreviewStatusText.Text = _retargetPreviewRenderer.Diagnostics;
                RetargetPreviewSwapChainPanel.Visibility = Visibility.Collapsed;
                RetargetPreviewImage.Visibility = Visibility.Visible;
                WriteableBitmap bitmap = _retargetPreviewSoftwareRenderer.Render(
                    _retargetPreviewScene,
                    width,
                    height,
                    _retargetPreviewCamera,
                    _retargetPreviewScene.ShadingMode,
                    _retargetPreviewScene.BackgroundStyle,
                    _retargetPreviewScene.LightingPreset,
                    _retargetPreviewScene.Wireframe,
                    _retargetPreviewScene.ShowGroundPlane);
                RetargetPreviewImage.Source = bitmap;
            }
        }
        else
        {
            _retargetPreviewRenderer.DetachPanel();
            RetargetPreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            RetargetPreviewImage.Visibility = Visibility.Visible;
            WriteableBitmap bitmap = _retargetPreviewSoftwareRenderer.Render(
                _retargetPreviewScene,
                width,
                height,
                _retargetPreviewCamera,
                _retargetPreviewScene.ShadingMode,
                _retargetPreviewScene.BackgroundStyle,
                _retargetPreviewScene.LightingPreset,
                _retargetPreviewScene.Wireframe,
                _retargetPreviewScene.ShowGroundPlane);
            RetargetPreviewImage.Source = bitmap;
        }

        await Task.CompletedTask;
    }

    private void ClearRetargetPreview()
    {
        _retargetPreviewMesh = null;
        _retargetPreviewScene.Clear();
        _retargetPreviewRenderer.DetachPanel();
        RetargetPreviewSwapChainPanel.Visibility = Visibility.Collapsed;
        RetargetPreviewImage.Source = null;
        RetargetPreviewImage.Visibility = Visibility.Collapsed;
        RetargetPreviewStatsText.Text = "Idle";
        RetargetPreviewStatusText.Text = "Pose preview will appear here once a source mesh or retarget mesh is loaded.";
    }

    private string BuildRetargetPreviewStatsText()
    {
        if (_retargetPreviewMesh is null)
            return "Idle";

        return $"Ready  |  Verts: {_retargetPreviewMesh.Vertices.Count:N0}  |  Tris: {_retargetPreviewMesh.Indices.Count / 3:N0}  |  Bones: {_retargetPreviewMesh.Bones.Count:N0}";
    }

    private void QueueRetargetPreviewRefresh()
    {
        _ = RefreshRetargetPreviewAsync();
    }

    private RetargetMesh? GetRetargetPosePreviewSourceMesh()
    {
        if (_processedMesh is not null && _processedMesh.Bones.Count > 0)
            return _processedMesh;

        if (_sourceMesh is not null && _sourceMesh.Bones.Count > 0)
            return _sourceMesh;

        if (_referenceMesh is not null && _referenceMesh.Bones.Count > 0)
            return _referenceMesh;

        if (_processedMesh is not null)
            return _processedMesh;

        if (_sourceMesh is not null)
            return _sourceMesh;

        return _referenceMesh;
    }

    private void SetEmptyState()
    {
        RetargetTitleText.Text = "No UPK loaded yet.";
        RetargetStatusText.Text = "Browse a UPK to inspect SkeletalMesh exports.";
        UpkStatusText.Text = "No UPK selected.";
        _suppressLodRefresh = true;
        LodComboBox.Items.Clear();
        LodComboBox.SelectedIndex = -1;
        _suppressLodRefresh = false;
        _selectedLodIndex = 0;
        SourceMeshStatusText.Text = "Import a source mesh to begin retargeting.";
        SourceMeshSummaryText.Text = "No source mesh loaded.";
        WorkflowStatusText.Text = "Import a source mesh, then run map/scale/transfer.";
        RetargetSummaryText.Text = "Retarget inspection details will appear here once a SkeletalMesh export is selected.";
        TargetMeshText.Text = "Select a SkeletalMesh export to view its LOD and bone summary.";
        TargetMeshStatusText.Text = "Selected SkeletalMesh export will appear here once the UPK is loaded.";
        DetailRows.Clear();
        MappingRows.Clear();
        ValidationRows.Clear();
        AnimationTransferRows.Clear();
        BatchRetargetRows.Clear();
        MirrorFixRows.Clear();
        TransformSolverRows.Clear();
        ClearRetargetPreview();
        _referenceMesh = null;
        _referenceSkeleton = null;
        _sourceMesh = null;
        _processedMesh = null;
        _selectedExportPath = null;
        _batchFolderPath = null;
        _playerSkeleton = null;
        _animSet = null;
        _animSetDisplayName = null;
        _texturePaths.Clear();
        _boneMapping = null;
        _manualMappingOverrides.Clear();
        _selectedPosePreset = RetargetPosePreset.BindPose;
        SkeletonStatusText.Text = "No player skeleton imported.";
        SkeletonPathTextBox.Text = string.Empty;
        AnimSetStatusText.Text = "No AnimSet imported.";
        TexturesStatusText.Text = "No textures imported.";
        MappingStatusText.Text = "Run Auto Map Bones to inspect the source-to-target bone mapping.";
        MappingProfileStatusText.Text = "Save or load a retarget mapping profile in the Profiles/Retargeting folder.";
        MappingProfileNameTextBox.Text = string.Empty;
        ManualOverrideSourceTextBox.Text = string.Empty;
        ManualOverrideTargetTextBox.Text = string.Empty;
        ValidationStatusText.Text = "Validate the source mesh against the selected target skeleton.";
        AnimationTransferStatusText.Text = "Transfer notifies and curves from the current AnimSet into the retargeted output.";
        BatchRetargetStatusText.Text = "Choose a folder of UPKs and retarget each one using the current mesh and export selection.";
        MirrorFixStatusText.Text = "Run Auto Fix Mirroring when the source mesh appears flipped or backfacing.";
        TransformSolverStatusText.Text = "Run Stabilize Transforms to clean unstable matrices before transfer and export.";
        BatchFolderTextBox.Text = string.Empty;
        PosePreviewStatusText.Text = "Pose preview is ready after weight transfer or one-click bind.";
        CompatibilityStatusText.Text = "Apply UE3 fixes before exporting or replacing the mesh.";
        OutputStatusText.Text = "Export FBX or replace the selected UPK mesh once the workflow is ready.";
        UpdateRetargetContextSummary();
    }

    private void OpenAnimationPreviewWindow()
    {
        RetargetMesh? previewSource = GetRetargetAnimationPlaybackSourceMesh();
        UAnimSet? previewAnimSet = _animSet ?? previewSource?.AnimSet;
        if (_animationPreviewWindow is null)
        {
            _animationPreviewWindow = new RetargetAnimationPreviewWindow();
            _animationPreviewWindow.Closed += AnimationPreviewWindow_Closed;
        }

        _animationPreviewWindow.SetPreviewSource(previewSource, previewAnimSet, _selectedPosePreset);
        _animationPreviewWindow.Activate();
    }

    private RetargetMesh? GetRetargetAnimationPlaybackSourceMesh()
    {
        if (HasRetargetPosePreviewData(_referenceMesh))
            return _referenceMesh;

        if (HasRetargetPosePreviewData(_processedMesh) && _processedMesh?.AnimSet is not null)
            return _processedMesh;

        if (HasRetargetPosePreviewData(_sourceMesh) && _sourceMesh?.AnimSet is not null)
            return _sourceMesh;

        return GetRetargetPosePreviewSourceMesh();
    }

    private static bool HasRetargetPosePreviewData(RetargetMesh? mesh)
    {
        return mesh is not null &&
               mesh.Bones.Count > 0 &&
               mesh.Sections.Any(static section => section.Vertices.Any(static vertex => vertex.Weights.Count > 0));
    }

    private void AnimationPreviewWindow_Closed(object sender, WindowEventArgs args)
    {
        _animationPreviewWindow = null;
    }

    private void AppendWorkflowLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (DispatcherQueue.HasThreadAccess)
        {
            RetargetStatusText.Text = message;
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!string.IsNullOrWhiteSpace(message))
                RetargetStatusText.Text = message;
        });
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        OperationProgressBar.IsIndeterminate = busy;
        if (!busy)
            OperationProgressBar.Value = 0;

        if (!string.IsNullOrWhiteSpace(message))
            OperationStatusText.Text = message;
        else if (busy)
            OperationStatusText.Text = "Working...";
        else
            OperationStatusText.Text = "Ready.";
    }

    private void ReportProgress(double value, double maximum, string message)
    {
        OperationProgressBar.IsIndeterminate = false;
        OperationProgressBar.Maximum = maximum <= 0 ? 100 : maximum;
        OperationProgressBar.Value = Math.Clamp(value, 0, OperationProgressBar.Maximum);
        if (!string.IsNullOrWhiteSpace(message))
            OperationStatusText.Text = message;
    }

    private void UpdateRetargetContextSummary()
    {
        string source = _sourceMesh is null
            ? "Source: none"
            : $"Source: {Path.GetFileNameWithoutExtension(_sourceMesh.SourcePath)}";
        string skeleton = _playerSkeleton is null
            ? "Player Skeleton: none"
            : $"Player Skeleton: {Path.GetFileNameWithoutExtension(_playerSkeleton.SourcePath)}";
        string animSet = string.IsNullOrWhiteSpace(_animSetDisplayName)
            ? "AnimSet: none"
            : $"AnimSet: {Path.GetFileNameWithoutExtension(_animSetDisplayName)}";
        string lod = $"LOD: {_selectedLodIndex}";
        string pose = $"Pose: {_selectedPosePreset}";

        RetargetContextText.Text = $"{source} | {skeleton} | {animSet} | {lod} | {pose}";
    }

    private void RefreshMappingRows()
    {
        MappingRows.Clear();
        if (_sourceMesh is null)
        {
            MappingStatusText.Text = "Run Auto Map Bones to inspect the source-to-target bone mapping.";
            return;
        }

        int mappedCount = 0;
        int unmappedCount = 0;
        foreach (RetargetBone sourceBone in _sourceMesh.Bones)
        {
            string mappedTarget = "(unmapped)";
            string? target = null;
            bool mapped = _boneMapping is not null &&
                _boneMapping.Mapping.TryGetValue(sourceBone.Name, out target) &&
                !string.IsNullOrWhiteSpace(target);
            if (mapped && !string.IsNullOrWhiteSpace(target))
                mappedTarget = target;

            MappingRows.Add(new RetargetDetailRow
            {
                Name = sourceBone.Name,
                Value = mappedTarget
            });

            if (mapped)
                mappedCount++;
            else
                unmappedCount++;
        }

        MappingStatusText.Text = unmappedCount == 0
            ? $"Mapped {mappedCount} bone(s)."
            : $"Mapped {mappedCount} bone(s); {unmappedCount} unmapped.";
    }

    private void SetLodOptions(int lodCount)
    {
        _suppressLodRefresh = true;
        LodComboBox.Items.Clear();

        if (lodCount <= 0)
        {
            LodComboBox.Items.Add("LOD 0");
            LodComboBox.SelectedIndex = 0;
            _selectedLodIndex = 0;
            _suppressLodRefresh = false;
            return;
        }

        for (int i = 0; i < lodCount; i++)
            LodComboBox.Items.Add($"LOD {i}");

        LodComboBox.SelectedIndex = Math.Clamp(_selectedLodIndex, 0, lodCount - 1);
        _selectedLodIndex = LodComboBox.SelectedIndex;
        _suppressLodRefresh = false;
    }

    private void RotateRetargetSourceMesh(float degrees)
    {
        RotateRetargetSourceMeshAroundAxis("up", degrees);
    }

    private void RotateRetargetSourceMeshAroundAxis(string axisName, float degrees)
    {
        if (_sourceMesh is null)
        {
            WorkflowStatusText.Text = "Import a source mesh first.";
            return;
        }

        SkeletonDefinition? rotationSkeleton = _playerSkeleton ?? _referenceSkeleton;
        if (rotationSkeleton is null)
        {
            WorkflowStatusText.Text = "Import a player skeleton or load a reference UPK first.";
            return;
        }

        try
        {
            OrientationProcessor processor = new();
            Vector3 axis = processor.GetTargetAxis(rotationSkeleton, axisName);
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI * degrees / 180.0f);
            _sourceMesh = processor.ApplyRotation(_sourceMesh, rotation, AppendWorkflowLog);
            _processedMesh = null;
            SourceMeshSummaryText.Text = $"{_sourceMesh.MeshName} | Verts: {_sourceMesh.VertexCount} | Tris: {_sourceMesh.TriangleCount} | Bones: {_sourceMesh.Bones.Count}";
            WorkflowStatusText.Text = $"Applied {degrees:0.#} degree rotation around {axisName}.";
            RetargetSummaryText.Text = "Manual rotation applied to the source mesh.";
        }
        catch (Exception ex)
        {
            WorkflowStatusText.Text = $"Manual rotation failed while adjusting the source mesh orientation: {ex.Message}";
            AppendWorkflowLog($"Manual rotation failed while adjusting the source mesh orientation: {ex.Message}");
        }
    }

    private static SkeletonDefinition BuildSkeletonDefinitionFromSkeletalMesh(string meshExportPath, USkeletalMesh skeletalMesh)
    {
        SkeletonDefinition skeleton = new()
        {
            SourcePath = meshExportPath
        };

        List<Matrix4x4> globals = new(skeletalMesh.RefSkeleton.Count);
        for (int i = 0; i < skeletalMesh.RefSkeleton.Count; i++)
        {
            FMeshBone bone = skeletalMesh.RefSkeleton[i];
            Matrix4x4 rawLocal = bone.BonePos.ToMatrix();
            Matrix4x4 rawGlobal = bone.ParentIndex >= 0 && bone.ParentIndex < globals.Count
                ? rawLocal * globals[bone.ParentIndex]
                : rawLocal;
            globals.Add(rawGlobal);

            skeleton.Bones.Add(new RetargetBone
            {
                Name = bone.Name?.Name ?? $"Bone_{i}",
                ParentIndex = bone.ParentIndex,
                LocalTransform = ConvertRetargetTransform(rawLocal),
                GlobalTransform = ConvertRetargetTransform(rawGlobal)
            });
        }

        skeleton.RebuildBoneLookup();
        return skeleton;
    }

    private static Matrix4x4 ConvertRetargetTransform(Matrix4x4 value)
    {
        return new Matrix4x4(
            value.M11, value.M13, value.M12, value.M14,
            value.M31, value.M33, value.M32, value.M34,
            value.M21, value.M23, value.M22, value.M24,
            value.M41, value.M43, value.M42, value.M44);
    }

    public sealed class RetargetExportViewModel
    {
        public int TableIndex { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string PathName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string MeshSummary { get; set; } = string.Empty;
    }

    public sealed class RetargetDetailRow
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string DisplayText => $"{Name}: {Value}";
    }
}

