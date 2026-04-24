using System.Numerics;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Rendering;
using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Rendering;

internal sealed class D3D11Renderer : IDisposable
{
    private readonly MeshPreviewD3D11Renderer renderer = new();
    private readonly MeshPreviewScene previewScene = new();
    private readonly D3D11DeviceManager deviceManager = new();
    private Scene? scene;
    private Camera? camera;

    public D3D11Renderer()
    {
        renderer.RenderCompleted += Renderer_RenderCompleted;
    }

    public event EventHandler? RenderCompleted;

    public string Diagnostics => renderer.Diagnostics;

    public bool LastRenderSucceeded => renderer.LastRenderSucceeded;

    public void AttachToPanel(SwapChainPanel panel)
    {
        renderer.AttachToPanel(panel, panel.DispatcherQueue);
    }

    public void DetachPanel()
    {
        renderer.DetachPanel();
    }

    public void SetFrame(Scene scene, Camera camera)
    {
        this.scene = scene;
        this.camera = camera;
    }

    public void Render(double width, double height)
    {
        if (scene is null || camera is null)
            return;

        BuildTexturedScene(scene);
        float aspect = (float)(width / Math.Max(1.0, height));
        Matrix4x4 view = camera.GetViewMatrix();
        Matrix4x4 projection = camera.GetProjectionMatrix(aspect);
        renderer.SetFrame(previewScene, view, projection);
    }

    private void Renderer_RenderCompleted(object? sender, EventArgs e)
    {
        RenderCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void BuildTexturedScene(Scene source)
    {
        previewScene.Clear();
        previewScene.DisplayMode = MeshPreviewDisplayMode.Overlay;
        previewScene.ShadingMode = MeshPreviewShadingMode.GameApprox;
        previewScene.BackgroundStyle = MeshPreviewBackgroundStyle.DarkGradient;
        previewScene.LightingPreset = MeshPreviewLightingPreset.Neutral;
        previewScene.MaterialChannel = MeshPreviewMaterialChannel.FullMaterial;
        previewScene.MaterialPreviewEnabled = true;
        previewScene.ShowGroundPlane = source.ShowGroundPlane;

        MeshPreviewMesh? meshA = source.MeshNodeA.PreviewMesh;
        MeshPreviewMesh? meshB = source.MeshNodeB.PreviewMesh;

        previewScene.SetFbxMesh(source.MeshNodeA.IsVisible ? meshA : null);
        previewScene.SetUe3Mesh(source.MeshNodeB.IsVisible ? meshB : null);

        ApplySectionTextures(previewScene, meshA, meshB);

        previewScene.ShowFbxMesh = source.MeshNodeA.IsVisible && meshA is not null;
        previewScene.ShowUe3Mesh = source.MeshNodeB.IsVisible && meshB is not null;
        previewScene.Wireframe = source.MeshNodeA.IsWireframe || source.MeshNodeB.IsWireframe;
        previewScene.DisableBackfaceCullingForFbx = source.MeshNodeA.IsGhosted;
        previewScene.DisableBackfaceCullingForUe3 = source.MeshNodeB.IsGhosted;
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

    public void Dispose()
    {
        renderer.RenderCompleted -= Renderer_RenderCompleted;
        renderer.Dispose();
        deviceManager.Dispose();
    }
}

