namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class MeshValidationSummary
{
    public string MeshName { get; set; } = string.Empty;

    public int InvalidTriangleCount { get; set; }

    public int InvalidWeightCount { get; set; }

    public int MissingBoneReferenceCount { get; set; }

    public bool HasBounds { get; set; }
}

