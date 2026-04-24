using System;
using System.Numerics;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Rendering;

public sealed class MaterialPreviewScene : IDisposable
{
    private MaterialPreviewMesh? previewMesh;

    internal MeshPreviewScene NativeScene { get; } = new();

    internal MeshPreviewCamera Camera { get; } = new();

    public MaterialPreviewMesh? PreviewMesh => previewMesh;

    public MaterialPreviewMesh EnsurePreviewMesh()
    {
        throw new InvalidOperationException("MaterialEditor preview mesh must be provided by the selected material source mesh.");
    }

    public void ApplyConfig(MaterialPreviewConfig? config)
    {
        MeshPreviewMaterialChannel channel = MeshPreviewMaterialChannel.FullMaterial;
        if (!string.IsNullOrWhiteSpace(config?.MaterialChannel) &&
            Enum.TryParse(config.MaterialChannel, ignoreCase: true, out MeshPreviewMaterialChannel parsedChannel))
        {
            channel = parsedChannel;
        }

        NativeScene.BackgroundStyle = MeshPreviewBackgroundStyle.DarkGradient;
        NativeScene.DisplayMode = MeshPreviewDisplayMode.Ue3Only;
        NativeScene.MaterialChannel = channel;
        NativeScene.ShadingMode = MeshPreviewShadingMode.GameApprox;
        NativeScene.ShowFbxMesh = false;
        NativeScene.ShowUe3Mesh = true;
        NativeScene.ShowGroundPlane = false;
        NativeScene.MaterialPreviewEnabled = true;
        NativeScene.DisableBackfaceCullingForUe3 = false;
        NativeScene.AmbientLight = config is null ? 0.75f : Math.Clamp(config.LightIntensity * 0.8f, 0.25f, 1.5f);
    }

    public void SetPreviewMesh(MaterialPreviewMesh mesh, bool resetCamera = true)
    {
        if (!ReferenceEquals(previewMesh, mesh))
            previewMesh?.Dispose();

        previewMesh = mesh;
        NativeScene.SetUe3Mesh(mesh.NativeMesh);

        if (resetCamera)
            ResetCamera();
    }

    public void ResetCamera()
    {
        if (previewMesh is null)
            return;

        Camera.Reset(previewMesh.NativeMesh.Center, MathF.Max(1.0f, previewMesh.NativeMesh.Radius));
    }

    public void Clear()
    {
        NativeScene.Clear();
        previewMesh?.Dispose();
        previewMesh = null;
    }

    public void Dispose()
    {
        Clear();
    }
}

