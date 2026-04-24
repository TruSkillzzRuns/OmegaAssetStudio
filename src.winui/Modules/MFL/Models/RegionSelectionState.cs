using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class RegionSelectionState
{
    public string Mode { get; set; } = string.Empty;

    public int TriangleIndex { get; set; } = -1;

    public int BoneIndex { get; set; } = -1;

    public string BoneName { get; set; } = string.Empty;

    public int SectionIndex { get; set; } = -1;

    public Vector3 HitPoint { get; set; } = Vector3.Zero;

    public List<int> TriangleIndices { get; set; } = [];

    public List<int> VertexIndices { get; set; } = [];
}

