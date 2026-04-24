namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class LODGroup
{
    public int LevelIndex { get; set; }

    public float ScreenSize { get; set; } = 1.0f;

    public List<int> TriangleIndices { get; set; } = [];

    public LODGroup Clone() => new()
    {
        LevelIndex = LevelIndex,
        ScreenSize = ScreenSize,
        TriangleIndices = [.. TriangleIndices]
    };
}

