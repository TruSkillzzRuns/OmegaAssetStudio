using System.Collections.Generic;
using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;

public sealed class NeutralMesh
{
    public string Name { get; set; } = string.Empty;
    public bool IsSkeletal { get; set; }
    public List<NeutralVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
    public List<NeutralSection> Sections { get; } = [];
    public List<NeutralMaterialSlot> MaterialSlots { get; } = [];
    public List<NeutralLod> Lods { get; } = [];
    public List<NeutralSocket> Sockets { get; } = [];
    public NeutralSkeleton? Skeleton { get; set; }
    public NeutralBoundingBox Bounds { get; set; } = new(Vector3.Zero, Vector3.Zero);
}

public sealed record NeutralVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector3 Tangent,
    Vector3 Bitangent,
    Vector2 TexCoord,
    NeutralBoneWeight[] Weights);

public sealed record NeutralBoneWeight(string BoneName, int BoneIndex, float Weight);

public sealed record NeutralSection(string Name, int MaterialIndex, int TriangleStart, int TriangleCount);

public sealed record NeutralMaterialSlot(string Name, int MaterialIndex, string? MaterialPath);

public sealed record NeutralLod(int Level, int VertexStart, int VertexCount, int IndexStart, int IndexCount);

public sealed record NeutralSocket(string SocketName, string BoneName, Vector3 Location, Vector3 Rotation, Vector3 Scale);

public sealed record NeutralBoundingBox(Vector3 Min, Vector3 Max);

