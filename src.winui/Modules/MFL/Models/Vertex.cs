using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class Vertex
{
    public Vector3 Position { get; set; } = Vector3.Zero;

    public Vector3 Normal { get; set; } = Vector3.UnitY;

    public Vector3 Tangent { get; set; } = Vector3.UnitX;

    public Vector3 Bitangent { get; set; } = Vector3.UnitZ;

    public int SectionIndex { get; set; }

    public int MaterialSlotIndex { get; set; }

    public List<Vector2> UVs { get; set; } = [];

    public List<BoneWeight> Weights { get; set; } = [];

    public Vertex Clone() => new()
    {
        Position = Position,
        Normal = Normal,
        Tangent = Tangent,
        Bitangent = Bitangent,
        SectionIndex = SectionIndex,
        MaterialSlotIndex = MaterialSlotIndex,
        UVs = UVs.Select(uv => uv).ToList(),
        Weights = Weights.Select(weight => weight.Clone()).ToList()
    };
}

