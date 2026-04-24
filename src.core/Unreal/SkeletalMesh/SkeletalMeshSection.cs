namespace OmegaAssetStudio.Unreal.SkeletalMesh;

public class SkeletalMeshSection
{
    public int MaterialIndex { get; set; }

    public int BaseIndex { get; set; }

    public int NumTriangles { get; set; }

    public int ChunkIndex { get; set; }

    public List<int> BoneMap { get; } = [];

    public int OriginalDataSectionIndex { get; set; }
}

