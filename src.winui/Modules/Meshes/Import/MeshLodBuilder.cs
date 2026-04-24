using OmegaAssetStudio.Unreal.SkeletalMesh;

namespace OmegaAssetStudio.WinUI.Modules.Meshes.Import;

public class MeshLodBuilder
{
    private readonly MeshSectionBuilder sectionBuilder = new();
    private readonly MeshVertexBuilder vertexBuilder = new();

    public SkeletalMeshLodModel BuildOrUpdateLod(SkeletalMesh skeletalMesh, FBXMeshData meshData, int lodIndex)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(meshData);

        FBXLodData lodData = meshData.Lods.FirstOrDefault(item => item.LodIndex == lodIndex) ?? new FBXLodData
        {
            LodIndex = lodIndex,
            Name = $"LOD{lodIndex}"
        };

        SkeletalMeshLodModel lodModel = EnsureLodModel(skeletalMesh, lodIndex);
        PopulateLodModel(lodModel, skeletalMesh, meshData, lodData);
        return lodModel;
    }

    public IReadOnlyList<SkeletalMeshLodModel> BuildOrUpdateAllLods(SkeletalMesh skeletalMesh, FBXMeshData meshData)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(meshData);

        List<SkeletalMeshLodModel> results = [];
        IEnumerable<FBXLodData> lods = meshData.Lods.Count > 0
            ? meshData.Lods.OrderBy(item => item.LodIndex)
            : [new FBXLodData { LodIndex = 0, Name = "LOD0" }];

        foreach (FBXLodData lodData in lods)
            results.Add(BuildOrUpdateLod(skeletalMesh, meshData, lodData.LodIndex));

        return results;
    }

    private static SkeletalMeshLodModel EnsureLodModel(SkeletalMesh skeletalMesh, int lodIndex)
    {
        while (skeletalMesh.LODModels.Count <= lodIndex)
            skeletalMesh.LODModels.Add(new SkeletalMeshLodModel { LodIndex = skeletalMesh.LODModels.Count });

        SkeletalMeshLodModel lodModel = skeletalMesh.LODModels[lodIndex];
        lodModel.LodIndex = lodIndex;
        return lodModel;
    }

    private void PopulateLodModel(SkeletalMeshLodModel lodModel, SkeletalMesh skeletalMesh, FBXMeshData meshData, FBXLodData lodData)
    {
        lodModel.Sections.Clear();
        lodModel.Chunks.Clear();
        lodModel.VertexBuffer.Vertices.Clear();
        lodModel.IndexBuffer.Clear();
        lodModel.RequiredBones.Clear();
        lodModel.ActiveBoneIndices.Clear();

        List<FBXSectionData> sections = meshData.Sections
            .Where(item => item.LodIndex == lodData.LodIndex)
            .OrderBy(item => item.SectionIndex)
            .ToList();

        if (sections.Count == 0 && meshData.Vertices.Count > 0 && meshData.Indices.Count > 0)
            sections = CreateFallbackSections(meshData, lodData.LodIndex);

        int vertexBase = 0;
        int indexBase = 0;

        foreach (FBXSectionData sectionData in sections)
        {
            int sectionVertexStart = NormalizeStart(sectionData.VertexStart, meshData.Vertices.Count);
            int sectionVertexCount = NormalizeCount(sectionData.VertexCount, meshData.Vertices.Count, sectionVertexStart);
            int sectionIndexStart = NormalizeStart(sectionData.IndexStart, meshData.Indices.Count);
            int sectionIndexCount = NormalizeCount(sectionData.IndexCount, meshData.Indices.Count, sectionIndexStart);

            if (sectionVertexCount <= 0)
                sectionVertexCount = meshData.Vertices.Count;

            if (sectionIndexCount <= 0)
                sectionIndexCount = meshData.Indices.Count;

            List<FBXVertexData> sourceVertices = meshData.Vertices
                .Skip(sectionVertexStart)
                .Take(sectionVertexCount)
                .ToList();

            if (sourceVertices.Count == 0)
                sourceVertices = meshData.Vertices.ToList();

            Dictionary<int, int> vertexMap = vertexBuilder.AppendVertices(
                lodModel.VertexBuffer,
                sourceVertices,
                meshData.BoneWeights,
                sectionVertexStart);

            List<int> sourceIndices = meshData.Indices
                .Skip(sectionIndexStart)
                .Take(sectionIndexCount)
                .ToList();

            if (sourceIndices.Count == 0)
                sourceIndices = meshData.Indices.ToList();

            List<int> remappedIndices = [];
            foreach (int index in sourceIndices)
            {
                if (vertexMap.TryGetValue(index, out int remapped))
                    remappedIndices.Add(remapped);
                else if (index >= sectionVertexStart && index < sectionVertexStart + sourceVertices.Count)
                    remappedIndices.Add(vertexBase + (index - sectionVertexStart));
                else
                    remappedIndices.Add(Math.Max(0, Math.Min(vertexBase + sourceVertices.Count - 1, index)));
            }

            lodModel.IndexBuffer.AddRange(remappedIndices);

            SkeletalMeshSection section = sectionBuilder.BuildSection(
                skeletalMesh,
                sectionData,
                remappedIndices,
                indexBase,
                lodModel.Chunks.Count);
            if (section.BoneMap.Count == 0)
            {
                foreach (int boneIndex in sectionData.BoneIndices.OrderBy(value => value).Distinct())
                    section.BoneMap.Add(boneIndex);
            }
            lodModel.Sections.Add(section);

            SkeletalMeshChunk chunk = sectionBuilder.BuildChunk(sourceVertices, sectionData.BoneIndices, vertexBase);
            lodModel.Chunks.Add(chunk);

            foreach (int boneIndex in sectionData.BoneIndices)
            {
                if (!lodModel.RequiredBones.Contains(boneIndex))
                    lodModel.RequiredBones.Add(boneIndex);

                if (!lodModel.ActiveBoneIndices.Contains(boneIndex))
                    lodModel.ActiveBoneIndices.Add(boneIndex);
            }

            vertexBase += sourceVertices.Count;
            indexBase += remappedIndices.Count;
        }

        lodModel.NumVertices = lodModel.VertexBuffer.Vertices.Count;
        lodModel.NumTriangles = lodModel.IndexBuffer.Count / 3;
    }

    private static List<FBXSectionData> CreateFallbackSections(FBXMeshData meshData, int lodIndex)
    {
        FBXSectionData section = new()
        {
            LodIndex = lodIndex,
            SectionIndex = 0,
            Name = $"LOD{lodIndex}",
            MaterialIndex = 0,
            MaterialName = meshData.MaterialNames.FirstOrDefault() ?? string.Empty,
            VertexStart = 0,
            VertexCount = meshData.Vertices.Count,
            IndexStart = 0,
            IndexCount = meshData.Indices.Count
        };
        return [section];
    }

    private static int NormalizeStart(int value, int upperBound)
    {
        if (upperBound <= 0)
            return 0;

        return Math.Clamp(value, 0, Math.Max(0, upperBound - 1));
    }

    private static int NormalizeCount(int value, int upperBound, int start)
    {
        if (upperBound <= 0)
            return 0;

        if (value <= 0)
            return upperBound - start;

        return Math.Min(value, upperBound - start);
    }
}

