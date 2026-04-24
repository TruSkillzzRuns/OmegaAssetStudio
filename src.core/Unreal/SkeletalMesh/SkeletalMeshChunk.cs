namespace OmegaAssetStudio.Unreal.SkeletalMesh;

public class SkeletalMeshChunk
{
    public int BaseVertexIndex { get; set; }

    public int NumRigidVertices { get; set; }

    public int NumSoftVertices { get; set; }

    public List<int> BoneMap { get; } = [];

    public int MaxBoneInfluences { get; set; }
}

