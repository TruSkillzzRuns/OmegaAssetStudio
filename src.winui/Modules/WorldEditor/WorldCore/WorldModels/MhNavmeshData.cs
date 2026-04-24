namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

public sealed class MhNavmeshData
{
    public string Name { get; set; } = string.Empty;

    public string? SourceUpkPath { get; set; }

    public int PolygonCount { get; set; }

    public string? BoundsText { get; set; }

    public string? Notes { get; set; }
}

