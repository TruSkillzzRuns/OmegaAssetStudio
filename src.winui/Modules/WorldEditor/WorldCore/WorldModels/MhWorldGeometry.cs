namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

public sealed class MhWorldGeometry
{
    public string Name { get; set; } = string.Empty;

    public string? SourceUpkPath { get; set; }

    public string? MeshPath { get; set; }

    public string? MaterialPath { get; set; }

    public string? Category { get; set; }

    public string? TransformText { get; set; }

    public string? BoundsText { get; set; }

    public bool IsTerrain { get; set; }

    public bool IsBuilding { get; set; }
}

