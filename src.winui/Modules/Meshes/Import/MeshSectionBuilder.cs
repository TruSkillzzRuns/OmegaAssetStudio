using OmegaAssetStudio.Unreal.SkeletalMesh;

namespace OmegaAssetStudio.WinUI.Modules.Meshes.Import;

public class MeshSectionBuilder
{
    private readonly MeshMaterialMapper materialMapper = new();

    public SkeletalMeshSection BuildSection(
        SkeletalMesh skeletalMesh,
        FBXSectionData sectionData,
        IReadOnlyList<int> sectionIndices,
        int baseIndex,
        int chunkIndex)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(sectionData);
        ArgumentNullException.ThrowIfNull(sectionIndices);

        SkeletalMeshSection section = new()
        {
            MaterialIndex = materialMapper.Resolve(skeletalMesh, sectionData),
            BaseIndex = baseIndex,
            NumTriangles = sectionIndices.Count / 3,
            ChunkIndex = chunkIndex,
            OriginalDataSectionIndex = sectionData.SectionIndex
        };

        foreach (int boneIndex in sectionData.BoneIndices.OrderBy(value => value).Distinct())
            section.BoneMap.Add(boneIndex);

        return section;
    }

    public SkeletalMeshChunk BuildChunk(
        IReadOnlyList<FBXVertexData> vertices,
        IReadOnlyList<int> boneIndices,
        int baseVertexIndex)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(boneIndices);

        SkeletalMeshChunk chunk = new()
        {
            BaseVertexIndex = baseVertexIndex,
            NumRigidVertices = vertices.Count(vertex => vertex.Color.W >= 0.999f),
            NumSoftVertices = Math.Max(0, vertices.Count - vertices.Count(vertex => vertex.Color.W >= 0.999f)),
            MaxBoneInfluences = vertices.Count == 0 ? 0 : 4
        };

        foreach (int boneIndex in boneIndices.OrderBy(value => value).Distinct())
            chunk.BoneMap.Add(boneIndex);

        return chunk;
    }
}

