using OmegaAssetStudio.Unreal.SkeletalMesh;

namespace OmegaAssetStudio.WinUI.Modules.Meshes.Import;

public class MeshVertexBuilder
{
    public Dictionary<int, int> AppendVertices(
        SkeletalMeshVertexBuffer vertexBuffer,
        IReadOnlyList<FBXVertexData> sourceVertices,
        IReadOnlyList<FBXBoneWeightData> boneWeights,
        int sourceVertexStartIndex)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(sourceVertices);
        ArgumentNullException.ThrowIfNull(boneWeights);

        Dictionary<int, List<FBXBoneWeightData>> groupedWeights = boneWeights
            .GroupBy(weight => weight.VertexIndex)
            .ToDictionary(group => group.Key, group => group.ToList());

        Dictionary<int, int> mapping = new();
        for (int index = 0; index < sourceVertices.Count; index++)
        {
            FBXVertexData source = sourceVertices[index];
            SkeletalMeshVertex vertex = new()
            {
                Position = source.Position,
                Normal = source.Normal,
                Tangent = source.Tangent,
                Color = source.Color
            };
            vertex.UVs.Add(source.UV);

            if (groupedWeights.TryGetValue(sourceVertexStartIndex + index, out List<FBXBoneWeightData>? weights))
                ApplyWeights(vertex, weights);
            else
                SetRigidWeight(vertex);

            vertexBuffer.Vertices.Add(vertex);
            mapping[sourceVertexStartIndex + index] = vertexBuffer.Vertices.Count - 1;
        }

        return mapping;
    }

    public SkeletalMeshVertexBuffer BuildVertexBuffer(
        IReadOnlyList<FBXVertexData> sourceVertices,
        IReadOnlyList<FBXBoneWeightData> boneWeights,
        int sourceVertexStartIndex,
        out Dictionary<int, int> mapping)
    {
        SkeletalMeshVertexBuffer buffer = new();
        mapping = AppendVertices(buffer, sourceVertices, boneWeights, sourceVertexStartIndex);
        return buffer;
    }

    private static void ApplyWeights(SkeletalMeshVertex vertex, IReadOnlyList<FBXBoneWeightData> weights)
    {
        List<FBXBoneWeightData> orderedWeights = weights
            .Where(weight => weight.Weight > 0.0f)
            .OrderByDescending(weight => weight.Weight)
            .Take(4)
            .ToList();

        float totalWeight = orderedWeights.Sum(weight => weight.Weight);
        if (totalWeight <= 0.0f)
        {
            SetRigidWeight(vertex);
            return;
        }

        for (int index = 0; index < 4; index++)
        {
            if (index < orderedWeights.Count)
            {
                FBXBoneWeightData weight = orderedWeights[index];
                vertex.InfluenceBones[index] = weight.BoneIndex;
                vertex.InfluenceWeights[index] = weight.Weight / totalWeight;
            }
            else
            {
                vertex.InfluenceBones[index] = 0;
                vertex.InfluenceWeights[index] = 0.0f;
            }
        }
    }

    private static void SetRigidWeight(SkeletalMeshVertex vertex)
    {
        vertex.InfluenceBones[0] = 0;
        vertex.InfluenceWeights[0] = 1.0f;
        for (int index = 1; index < 4; index++)
        {
            vertex.InfluenceBones[index] = 0;
            vertex.InfluenceWeights[index] = 0.0f;
        }
    }
}

