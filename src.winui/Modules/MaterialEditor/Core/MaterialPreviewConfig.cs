using System.Numerics;
using OmegaAssetStudio.MeshPreview;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

public enum MaterialPreviewMeshType
{
    Sphere,
    Plane,
    Custom
}

public sealed class MaterialPreviewConfig
{
    public MaterialPreviewMeshType MeshType { get; set; } = MaterialPreviewMeshType.Custom;
    public string MaterialChannel { get; set; } = nameof(MeshPreviewMaterialChannel.FullMaterial);
    public Vector3 LightDirection { get; set; } = new(0.35f, -0.75f, -0.45f);
    public float LightIntensity { get; set; } = 1.0f;
    public Vector3 LightColor { get; set; } = new(1.0f, 1.0f, 1.0f);
    public Vector4 BackgroundColor { get; set; } = new(0.12f, 0.12f, 0.12f, 1.0f);
}

