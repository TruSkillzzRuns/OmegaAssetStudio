namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

public sealed class MhCollisionVolume
{
    public string Name { get; set; } = string.Empty;

    public string? ShapeType { get; set; }

    public string? SourceUpkPath { get; set; }

    public string? TransformText { get; set; }

    public string? BoundsText { get; set; }
}

