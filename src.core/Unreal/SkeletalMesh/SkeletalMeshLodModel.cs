namespace OmegaAssetStudio.Unreal.SkeletalMesh;

public class SkeletalMeshLodModel
{
    public int LodIndex { get; set; }

    public List<SkeletalMeshSection> Sections { get; } = [];

    public List<SkeletalMeshChunk> Chunks { get; } = [];

    public SkeletalMeshVertexBuffer VertexBuffer { get; set; } = new();

    public List<int> IndexBuffer { get; } = [];

    public List<int> RequiredBones { get; } = [];

    public List<int> ActiveBoneIndices { get; } = [];

    public int NumVertices { get; set; }

    public int NumTriangles { get; set; }
}

