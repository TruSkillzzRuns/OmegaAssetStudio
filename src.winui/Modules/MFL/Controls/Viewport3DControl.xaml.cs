using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Rendering;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;
using OmegaAssetStudio.WinUI.Modules.MFL.Rendering;
using Windows.Foundation;
using MflScene = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.Scene;
using MflCamera = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.Camera;
using MflMeshHitResult = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.MeshHitResult;
using ViewportRay = OmegaAssetStudio.WinUI.Modules.MFL.Viewport.ViewportRay;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Controls;

public sealed partial class Viewport3DControl : UserControl
{
    private readonly MeshPreviewD3D11Renderer texturedRenderer = new();
    private readonly MeshPreviewScene texturedScene = new();
    private MflScene? subscribedScene;
    private MeshNode? subscribedNodeA;
    private MeshNode? subscribedNodeB;
    private bool renderQueued;
    private bool isDragging;
    private bool wasClick;
    private bool selectionEligible;
    private Point dragStart;
    private Point lastPoint;
    private enum DragMode { None, Orbit, Pan }
    private DragMode dragMode = DragMode.None;

    public Viewport3DControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        texturedRenderer.RenderCompleted += TexturedRenderer_RenderCompleted;
    }

    public event EventHandler<MflMeshHitResult>? MeshHitSelected;
    public event EventHandler<string>? RenderDiagnosticsChanged;

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene),
        typeof(MflScene),
        typeof(Viewport3DControl),
        new PropertyMetadata(null, OnScenePropertyChanged));

    public static readonly DependencyProperty CameraProperty = DependencyProperty.Register(
        nameof(Camera),
        typeof(MflCamera),
        typeof(Viewport3DControl),
        new PropertyMetadata(null, OnCameraPropertyChanged));

    public MflScene? Scene
    {
        get => (MflScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public MflCamera? Camera
    {
        get => (MflCamera?)GetValue(CameraProperty);
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
        texturedRenderer.DetachPanel();
    }

    private void AttachRenderer()
    {
        texturedRenderer.AttachToPanel(RenderPanel, DispatcherQueue);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidateRender();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(RootGrid);
        wasClick = true;
        selectionEligible = point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed && !point.Properties.IsMiddleButtonPressed;
        dragStart = point.Position;
        lastPoint = point.Position;
        isDragging = true;
        dragMode = point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed ? DragMode.Pan : DragMode.Orbit;
        RootGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!isDragging || Camera is null)
            return;

        Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(RootGrid);
        Point current = point.Position;
        Vector2 delta = new((float)(current.X - lastPoint.X), (float)(current.Y - lastPoint.Y));
        if (Math.Abs(current.X - dragStart.X) > 2.0 || Math.Abs(current.Y - dragStart.Y) > 2.0)
            wasClick = false;

        if (dragMode == DragMode.Pan)
            Camera.Pan(delta.X, delta.Y);
        else
            Camera.Orbit(delta.X, delta.Y);

        lastPoint = current;
        InvalidateRender();
        e.Handled = true;
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!isDragging)
            return;

        isDragging = false;
        RootGrid.ReleasePointerCapture(e.Pointer);

        if (wasClick && selectionEligible)
            TryHitTest(e.GetCurrentPoint(RenderPanel).Position);

        dragMode = DragMode.None;
        selectionEligible = false;
        e.Handled = true;
    }

    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (Camera is null)
            return;

        Camera.Zoom(e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta);
        InvalidateRender();
        e.Handled = true;
    }

    private static void OnScenePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Viewport3DControl control = (Viewport3DControl)d;
        control.DetachSceneSubscriptions();
        control.AttachSceneSubscriptions();
        control.InvalidateRender();
    }

    private static void OnCameraPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Viewport3DControl control = (Viewport3DControl)d;
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
            || e.PropertyName == nameof(MflScene.ActiveMeshKey)
            || e.PropertyName == nameof(MflScene.GhostInactiveMesh)
            || e.PropertyName == nameof(MflScene.ShowGroundPlane))
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
        RenderDiagnosticsChanged?.Invoke(this, DescribeRenderState("Render attempt"));

        if (Scene is null || Camera is null)
        {
            HintTextBlock.Text = "Load Mesh A and Mesh B to view the unified viewport.";
            ActiveMeshTextBlock.Text = "No scene";
            texturedRenderer.DetachPanel();
            RenderDiagnosticsChanged?.Invoke(this, DescribeRenderState("Viewport detached: scene or camera missing."));
            return;
        }

        AttachRenderer();

        double width = Math.Max(1.0, RenderPanel.ActualWidth > 0 ? RenderPanel.ActualWidth : RootGrid.ActualWidth);
        double height = Math.Max(1.0, RenderPanel.ActualHeight > 0 ? RenderPanel.ActualHeight : RootGrid.ActualHeight);
        float aspect = (float)(width / Math.Max(1.0, height));

        BuildTexturedScene();

        Matrix4x4 view = Camera.GetViewMatrix();
        Matrix4x4 projection = Camera.GetProjectionMatrix(aspect);
        texturedRenderer.SetFrame(texturedScene, view, projection);
        RenderDiagnosticsChanged?.Invoke(this, DescribeRenderState("Frame submitted"));
        RenderDiagnosticsChanged?.Invoke(this, texturedRenderer.Diagnostics);
        UpdateOverlayText();

        string active = Scene.ActiveMeshKey == "MeshB" ? "Mesh B" : "Mesh A";
        string ghostState = Scene.GhostInactiveMesh ? "ghost mode on" : "ghost mode off";
        ActiveMeshTextBlock.Text = $"{active} active â€¢ {ghostState}";
        HintTextBlock.Text = "Left drag orbit â€¢ Right drag pan â€¢ Wheel zoom â€¢ Click a triangle to select a mesh";
    }

    private void BuildTexturedScene()
    {
        texturedScene.Clear();
        texturedScene.ShadingMode = MeshPreviewShadingMode.GameApprox;
        texturedScene.BackgroundStyle = MeshPreviewBackgroundStyle.DarkGradient;
        texturedScene.LightingPreset = MeshPreviewLightingPreset.Neutral;
        texturedScene.MaterialChannel = MeshPreviewMaterialChannel.BaseColor;
        texturedScene.MaterialPreviewEnabled = true;
        texturedScene.ShowSections = false;
        texturedScene.ShowGroundPlane = Scene?.ShowGroundPlane == true;
        texturedScene.AmbientLight = 0.9f;

        MeshPreviewMesh? meshA = Scene?.MeshNodeA.PreviewMesh;
        MeshPreviewMesh? meshB = Scene?.MeshNodeB.PreviewMesh;
        bool showMeshA = Scene?.MeshNodeA.IsVisible == true && meshA is not null;
        bool showMeshB = Scene?.MeshNodeB.IsVisible == true && meshB is not null;
        bool singleMesh = showMeshA ^ showMeshB;

        texturedScene.DisplayMode = singleMesh ? MeshPreviewDisplayMode.Ue3Only : MeshPreviewDisplayMode.Overlay;

        if (singleMesh)
        {
            MeshPreviewMesh? visibleMesh = showMeshA ? meshA : meshB;
            texturedScene.SetFbxMesh(null!);
            texturedScene.SetUe3Mesh(visibleMesh);
            texturedScene.FbxModelMatrix = Matrix4x4.Identity;
            texturedScene.Ue3ModelMatrix = showMeshA ? Scene!.MeshNodeA.WorldTransform : Scene!.MeshNodeB.WorldTransform;
            texturedScene.ShowFbxMesh = false;
            texturedScene.ShowUe3Mesh = visibleMesh is not null;
            texturedScene.DisableBackfaceCullingForFbx = false;
            texturedScene.DisableBackfaceCullingForUe3 = true;
            texturedScene.FbxFocusedSectionIndex = Scene!.MeshNodeA.HighlightedSectionIndex;
            texturedScene.Ue3FocusedSectionIndex = Scene!.MeshNodeB.HighlightedSectionIndex;
            ApplySectionTextures(texturedScene, null, visibleMesh);
        }
        else
        {
            if (showMeshA)
                texturedScene.SetFbxMesh(meshA);
            else
                texturedScene.SetFbxMesh(null!);

            if (showMeshB)
                texturedScene.SetUe3Mesh(meshB);
            else
                texturedScene.SetUe3Mesh(null!);

            texturedScene.FbxModelMatrix = Scene!.MeshNodeA.WorldTransform;
            texturedScene.Ue3ModelMatrix = Scene!.MeshNodeB.WorldTransform;

            ApplySectionTextures(texturedScene, meshA, meshB);

            texturedScene.ShowFbxMesh = showMeshA;
            texturedScene.ShowUe3Mesh = showMeshB;
            texturedScene.DisableBackfaceCullingForFbx = true;
            texturedScene.DisableBackfaceCullingForUe3 = true;
            texturedScene.FbxFocusedSectionIndex = Scene!.MeshNodeA.HighlightedSectionIndex;
            texturedScene.Ue3FocusedSectionIndex = Scene!.MeshNodeB.HighlightedSectionIndex;
        }

        texturedScene.Wireframe = (Scene?.MeshNodeA.IsWireframe == true) || (Scene?.MeshNodeB.IsWireframe == true);
    }

    private static void ApplySectionTextures(MeshPreviewScene scene, MeshPreviewMesh? meshA, MeshPreviewMesh? meshB)
    {
        if (meshA is not null)
        {
            foreach (MeshPreviewSection section in meshA.Sections)
            {
                if (section.GameMaterial?.Enabled != true)
                    continue;

                foreach ((MeshPreviewGameTextureSlot gameSlot, TexturePreviewTexture texture) in section.GameMaterial.Textures)
                {
                    TexturePreviewMaterialSlot previewSlot = ResolvePreviewMaterialSlot(gameSlot);
                    scene.SetFbxSectionMaterialTexture(section.Index, previewSlot, texture);
                }
            }
        }

        if (meshB is not null)
        {
            foreach (MeshPreviewSection section in meshB.Sections)
            {
                if (section.GameMaterial?.Enabled != true)
                    continue;

                foreach ((MeshPreviewGameTextureSlot gameSlot, TexturePreviewTexture texture) in section.GameMaterial.Textures)
                {
                    TexturePreviewMaterialSlot previewSlot = ResolvePreviewMaterialSlot(gameSlot);
                    scene.SetUe3SectionMaterialTexture(section.Index, previewSlot, texture);
                }
            }
        }
    }

    private static TexturePreviewMaterialSlot ResolvePreviewMaterialSlot(MeshPreviewGameTextureSlot slot)
    {
        return slot switch
        {
            MeshPreviewGameTextureSlot.Diffuse => TexturePreviewMaterialSlot.Diffuse,
            MeshPreviewGameTextureSlot.Normal => TexturePreviewMaterialSlot.Normal,
            MeshPreviewGameTextureSlot.Smspsk => TexturePreviewMaterialSlot.Mask,
            MeshPreviewGameTextureSlot.Espa => TexturePreviewMaterialSlot.Emissive,
            MeshPreviewGameTextureSlot.Smrr => TexturePreviewMaterialSlot.Mask,
            MeshPreviewGameTextureSlot.SpecColor => TexturePreviewMaterialSlot.Specular,
            _ => TexturePreviewMaterialSlot.Diffuse
        };
    }

    private void TryHitTest(Point point)
    {
        if (Scene is null || Camera is null || RenderPanel.ActualWidth <= 0 || RenderPanel.ActualHeight <= 0)
            return;

        MeshHitResult? bestHit = null;
        float bestDistance = float.MaxValue;

        foreach (MeshNode node in Scene.Nodes)
        {
            if (!node.IsVisible || node.Mesh is null)
                continue;

            MeshHitResult? hit = HitTestMesh(node, point);
            if (hit is null || hit.Distance >= bestDistance)
                continue;

            bestDistance = hit.Distance;
            bestHit = hit;
        }

        if (bestHit is not null)
            MeshHitSelected?.Invoke(this, bestHit);
    }

    private MflMeshHitResult? HitTestMesh(MeshNode node, Point point)
    {
        if (Scene is null || Camera is null || node.Mesh is null)
            return null;

        ViewportRay ray = Camera.CreateRay(point, RenderPanel.ActualWidth, RenderPanel.ActualHeight);
        Mesh mesh = node.Mesh;
        Matrix4x4 world = node.WorldTransform;
        float closest = float.MaxValue;
        int closestTriangle = -1;
        Vector3 hitPoint = Vector3.Zero;

        for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
        {
            Triangle triangle = mesh.Triangles[triangleIndex];
            if (triangle.A < 0 || triangle.B < 0 || triangle.C < 0
                || triangle.A >= mesh.Vertices.Count
                || triangle.B >= mesh.Vertices.Count
                || triangle.C >= mesh.Vertices.Count)
            {
                continue;
            }

            Vector3 a = Vector3.Transform(mesh.Vertices[triangle.A].Position, world);
            Vector3 b = Vector3.Transform(mesh.Vertices[triangle.B].Position, world);
            Vector3 c = Vector3.Transform(mesh.Vertices[triangle.C].Position, world);

            if (TryIntersectTriangle(ray.Origin, ray.Direction, a, b, c, out float distance, out Vector3 triangleHit) && distance < closest)
            {
                closest = distance;
                closestTriangle = triangleIndex;
                hitPoint = triangleHit;
            }
        }

        if (closestTriangle < 0)
            return null;

        return new MflMeshHitResult
        {
            MeshKey = ReferenceEquals(node, Scene.MeshNodeB) ? "MeshB" : "MeshA",
            TriangleIndex = closestTriangle,
            VertexIndex = -1,
            HitPoint = hitPoint,
            Distance = closest
        };
    }

    private void TexturedRenderer_RenderCompleted(object? sender, EventArgs e)
    {
        RenderDiagnosticsChanged?.Invoke(this, DescribeRenderState("Render completed"));
        RenderDiagnosticsChanged?.Invoke(this, texturedRenderer.Diagnostics);
        UpdateOverlayText();
        if (!texturedRenderer.LastRenderSucceeded || texturedRenderer.Diagnostics.Contains("Draws: FBX=0, UE3=0", StringComparison.OrdinalIgnoreCase))
            HintTextBlock.Text = $"{HintTextBlock.Text}\n{texturedRenderer.Diagnostics}";
    }

    private void UpdateOverlayText()
    {
        if (Scene is null)
            return;

        string active = Scene.ActiveMeshKey == "MeshB" ? "Mesh B" : "Mesh A";
        string ghostState = Scene.GhostInactiveMesh ? "ghost mode on" : "ghost mode off";
        ActiveMeshTextBlock.Text = $"{active} active â€¢ {ghostState}";
        HintTextBlock.Text = "Left drag orbit â€¢ Right drag pan â€¢ Wheel zoom â€¢ Click a triangle to select a mesh";
    }

    private string DescribeRenderState(string stage)
    {
        string sceneState = Scene is null ? "null" : "ready";
        string cameraState = Camera is null ? "null" : "ready";
        string panelSize = $"{RenderPanel.ActualWidth:0.##}x{RenderPanel.ActualHeight:0.##}";
        string rootSize = $"{RootGrid.ActualWidth:0.##}x{RootGrid.ActualHeight:0.##}";
        string meshAState = Scene is null
            ? "n/a"
            : $"mesh={(Scene.MeshNodeA.Mesh is null ? "null" : Scene.MeshNodeA.Mesh.Name)}, preview={(Scene.MeshNodeA.PreviewMesh is null ? "null" : "ready")}, base={(Scene.MeshNodeA.BasePreviewMesh is null ? "null" : "ready")}, visible={Scene.MeshNodeA.IsVisible}, wireframe={Scene.MeshNodeA.IsWireframe}, ghosted={Scene.MeshNodeA.IsGhosted}, highlighted={Scene.MeshNodeA.IsHighlighted}, world={Scene.MeshNodeA.WorldTransform}";
        string meshBState = Scene is null
            ? "n/a"
            : $"mesh={(Scene.MeshNodeB.Mesh is null ? "null" : Scene.MeshNodeB.Mesh.Name)}, preview={(Scene.MeshNodeB.PreviewMesh is null ? "null" : "ready")}, base={(Scene.MeshNodeB.BasePreviewMesh is null ? "null" : "ready")}, visible={Scene.MeshNodeB.IsVisible}, wireframe={Scene.MeshNodeB.IsWireframe}, ghosted={Scene.MeshNodeB.IsGhosted}, highlighted={Scene.MeshNodeB.IsHighlighted}, world={Scene.MeshNodeB.WorldTransform}";
        string active = Scene is null ? "n/a" : Scene.ActiveMeshKey;
        string ghost = Scene is null ? "n/a" : Scene.GhostInactiveMesh.ToString();
        string renderer = texturedRenderer.Diagnostics;
        return $"{stage}: scene={sceneState}, camera={cameraState}, panel={panelSize}, root={rootSize}, active={active}, ghostInactive={ghost}, meshA={meshAState}, meshB={meshBState}, renderer={renderer}";
    }

    private static bool TryIntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 hitPoint)
    {
        distance = 0.0f;
        hitPoint = Vector3.Zero;
        const float epsilon = 0.000001f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 h = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, h);

        if (det > -epsilon && det < epsilon)
            return false;

        float invDet = 1.0f / det;
        Vector3 s = origin - a;
        float u = invDet * Vector3.Dot(s, h);
        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = invDet * Vector3.Dot(direction, q);
        if (v < 0.0f || u + v > 1.0f)
            return false;

        float t = invDet * Vector3.Dot(edge2, q);
        if (t <= epsilon)
            return false;

        distance = t;
        hitPoint = origin + direction * t;
        return true;
    }
}

