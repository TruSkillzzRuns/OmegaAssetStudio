using System.Numerics;

namespace OmegaAssetStudio.Unreal.SkeletalMesh;

public class SkeletalMeshVertexBuffer
{
    public List<SkeletalMeshVertex> Vertices { get; } = [];
}

public class SkeletalMeshVertex
{
    public Vector3 Position { get; set; }

    public Vector3 Normal { get; set; }

    public Vector4 Tangent { get; set; }

    public List<Vector2> UVs { get; } = [];

    public Vector4 Color { get; set; } = Vector4.One;

    public int[] InfluenceBones { get; } = new int[4];

    public float[] InfluenceWeights { get; } = new float[4];
}

