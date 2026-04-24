using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Rendering;

public sealed partial class D3D11SwapChainHost : UserControl
{
    private readonly D3D11Renderer d3dRenderer = new();
    private readonly InputController inputController = new();
    private readonly Raycaster raycaster = new();
    private Scene? subscribedScene;
    private MeshNode? subscribedNodeA;
    private MeshNode? subscribedNodeB;
    private bool renderQueued;
    private bool rendererAttached;

    public D3D11SwapChainHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        d3dRenderer.RenderCompleted += D3dRenderer_RenderCompleted;
    }

    public event EventHandler<MeshHitResult>? MeshHitSelected;

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene),
        typeof(Scene),
        typeof(D3D11SwapChainHost),
        new PropertyMetadata(null, OnScenePropertyChanged));

    public static readonly DependencyProperty CameraProperty = DependencyProperty.Register(
        nameof(Camera),
        typeof(Camera),
        typeof(D3D11SwapChainHost),
        new PropertyMetadata(null, OnCameraPropertyChanged));

    public Scene? Scene
    {
        get => (Scene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public Camera? Camera
    {
        get => (Camera?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSceneSubscriptions();
        AttachRenderer();
        InvalidateRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSceneSubscriptions();
        d3dRenderer.DetachPanel();
        rendererAttached = false;
    }

    private void AttachRenderer()
    {
        if (rendererAttached || RenderPanel.XamlRoot is null)
            return;

        d3dRenderer.AttachToPanel(RenderPanel);
        rendererAttached = true;
    }

    private static void OnScenePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        D3D11SwapChainHost control = (D3D11SwapChainHost)d;
        control.DetachSceneSubscriptions();
        control.AttachSceneSubscriptions();
        control.InvalidateRender();
    }

    private static void OnCameraPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        D3D11SwapChainHost control = (D3D11SwapChainHost)d;
        control.InvalidateRender();
    }

    private void AttachSceneSubscriptions()
    {
        if (Scene is null || ReferenceEquals(subscribedScene, Scene))
            return;

        DetachSceneSubscriptions();
        subscribedScene = Scene;
        subscribedNodeA = Scene.MeshNodeA;
        subscribedNodeB = Scene.MeshNodeB;
        subscribedScene.PropertyChanged += Scene_PropertyChanged;
        subscribedNodeA.PropertyChanged += MeshNode_PropertyChanged;
        subscribedNodeB.PropertyChanged += MeshNode_PropertyChanged;
    }

    private void DetachSceneSubscriptions()
    {
        if (subscribedScene is not null)
            subscribedScene.PropertyChanged -= Scene_PropertyChanged;
        if (subscribedNodeA is not null)
            subscribedNodeA.PropertyChanged -= MeshNode_PropertyChanged;
        if (subscribedNodeB is not null)
            subscribedNodeB.PropertyChanged -= MeshNode_PropertyChanged;

        subscribedScene = null;
        subscribedNodeA = null;
        subscribedNodeB = null;
    }

    private void Scene_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || e.PropertyName == nameof(Scene.ActiveMeshKey)
            || e.PropertyName == nameof(Scene.GhostInactiveMesh))
        {
            InvalidateRender();
        }
    }

    private void MeshNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || e.PropertyName == nameof(MeshNode.Mesh)
            || e.PropertyName == nameof(MeshNode.BasePreviewMesh)
            || e.PropertyName == nameof(MeshNode.PreviewMesh)
            || e.PropertyName == nameof(MeshNode.Name)
            || e.PropertyName == nameof(MeshNode.Position)
            || e.PropertyName == nameof(MeshNode.RotationDegrees)
            || e.PropertyName == nameof(MeshNode.Scale)
            || e.PropertyName == nameof(MeshNode.IsVisible)
            || e.PropertyName == nameof(MeshNode.IsWireframe)
            || e.PropertyName == nameof(MeshNode.IsHighlighted)
            || e.PropertyName == nameof(MeshNode.IsGhosted)
            || e.PropertyName == nameof(MeshNode.SelectedTriangleIndex)
            || e.PropertyName == nameof(MeshNode.HighlightedTriangleIndices))
        {
            InvalidateRender();
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidateRender();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(RootGrid);
        inputController.BeginPointer(point.Position, point.Properties.IsLeftButtonPressed, point.Properties.IsRightButtonPressed, point.Properties.IsMiddleButtonPressed);
        RootGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        inputController.MovePointer(e.GetCurrentPoint(RootGrid).Position, Camera);
        InvalidateRender();
        e.Handled = true;
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        bool isClick = inputController.EndPointer();
        RootGrid.ReleasePointerCapture(e.Pointer);

        if (isClick)
            TryHitTest(e.GetCurrentPoint(RootGrid).Position);

        e.Handled = true;
    }

    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        inputController.Zoom(e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta, Camera);
        InvalidateRender();
        e.Handled = true;
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (inputController.HandleKeyDown(e.Key, Camera))
        {
            InvalidateRender();
            e.Handled = true;
        }
    }

    private void InvalidateRender()
    {
        if (renderQueued)
            return;

        renderQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            renderQueued = false;
            RenderScene();
        });
    }

    private void RenderScene()
    {
        if (Scene is null || Camera is null)
        {
            HintTextBlock.Text = "Load Mesh A and Mesh B to view the unified viewport.";
            ActiveMeshTextBlock.Text = "No scene";
            d3dRenderer.DetachPanel();
            return;
        }

        AttachRenderer();

        double width = Math.Max(1.0, RenderPanel.ActualWidth > 0 ? RenderPanel.ActualWidth : RootGrid.ActualWidth);
        double height = Math.Max(1.0, RenderPanel.ActualHeight > 0 ? RenderPanel.ActualHeight : RootGrid.ActualHeight);

        d3dRenderer.SetFrame(Scene, Camera);
        d3dRenderer.Render(width, height);

        string active = Scene.ActiveMeshKey == "MeshB" ? "Mesh B" : "Mesh A";
        string ghostState = Scene.GhostInactiveMesh ? "ghost mode on" : "ghost mode off";
        ActiveMeshTextBlock.Text = $"{active} active â€¢ {ghostState}";
        HintTextBlock.Text = "Left drag orbit â€¢ Right drag pan â€¢ Wheel zoom â€¢ Click a triangle to select a mesh";
    }

    private void TryHitTest(Point point)
    {
        if (Scene is null || Camera is null || RootGrid.ActualWidth <= 0 || RootGrid.ActualHeight <= 0)
            return;

        if (raycaster.TryHitTest(Scene, Camera, point, RootGrid.ActualWidth, RootGrid.ActualHeight, out MeshHitResult? hit) && hit is not null)
            MeshHitSelected?.Invoke(this, hit);
    }

    private void D3dRenderer_RenderCompleted(object? sender, EventArgs e)
    {
    }
}

