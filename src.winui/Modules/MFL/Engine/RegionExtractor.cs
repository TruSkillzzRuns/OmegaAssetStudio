using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class RegionExtractor
{
    public Mesh Extract(Mesh source, RegionSelectionResult selection)
    {
        Mesh extracted = new()
        {
            Name = $"{source.Name}_Region",
            SourcePath = source.SourcePath
        };

        HashSet<int> selectedVertices = selection.VertexIndices.Count > 0
            ? selection.VertexIndices.ToHashSet()
            : [];

        if (selectedVertices.Count == 0)
        {
            foreach (int triangleIndex in selection.TriangleIndices)
            {
                if (triangleIndex < 0 || triangleIndex >= source.Triangles.Count)
                    continue;

                Triangle triangle = source.Triangles[triangleIndex];
                selectedVertices.Add(triangle.A);
                selectedVertices.Add(triangle.B);
                selectedVertices.Add(triangle.C);
            }
        }

        if (selectedVertices.Count == 0)
            return extracted;

        Dictionary<int, int> vertexMap = [];
        Dictionary<int, int> boneMap = [];
        HashSet<int> usedBones = [];
        List<int> orderedVertices = selectedVertices.OrderBy(value => value).ToList();

        foreach (int vertexIndex in orderedVertices)
        {
            if (vertexIndex < 0 || vertexIndex >= source.Vertices.Count)
                continue;

            Vertex vertex = source.Vertices[vertexIndex].Clone();
            vertex.Weights = NormalizeBoneWeights(vertex.Weights, source);
            extracted.Vertices.Add(vertex);
            vertexMap[vertexIndex] = extracted.Vertices.Count - 1;

            foreach (BoneWeight weight in vertex.Weights)
            {
                if (weight.BoneIndex >= 0)
                    usedBones.Add(weight.BoneIndex);
            }
        }

        foreach (int boneIndex in usedBones.OrderBy(value => value))
        {
            if (boneIndex < 0 || boneIndex >= source.Bones.Count)
                continue;

            boneMap[boneIndex] = extracted.Bones.Count;
            extracted.Bones.Add(source.Bones[boneIndex].Clone());
        }

        extracted.MaterialSlots.AddRange(source.MaterialSlots.Select(slot => slot.Clone()));

        foreach (UVSet uvSet in source.UVSets)
        {
            extracted.UVSets.Add(new UVSet
            {
                ChannelIndex = uvSet.ChannelIndex,
                Name = uvSet.Name,
                Coordinates = []
            });
        }

        for (int vertexIndex = 0; vertexIndex < extracted.Vertices.Count; vertexIndex++)
        {
            int sourceIndex = orderedVertices[vertexIndex];
            Vertex sourceVertex = source.Vertices[sourceIndex];
            Vertex extractedVertex = extracted.Vertices[vertexIndex];
            extractedVertex.Weights = sourceVertex.Weights
                .Where(weight => boneMap.ContainsKey(weight.BoneIndex))
                .Select(weight => new BoneWeight
                {
                    BoneIndex = boneMap[weight.BoneIndex],
                    BoneName = source.Bones[weight.BoneIndex].Name,
                    Weight = weight.Weight
                })
                .ToList();

            for (int uvIndex = 0; uvIndex < sourceVertex.UVs.Count; uvIndex++)
            {
                if (uvIndex >= extracted.UVSets.Count)
                    break;

                extracted.UVSets[uvIndex].Coordinates.Add(sourceVertex.UVs[uvIndex]);
            }
        }

        List<int> selectedTriangleIndices = selection.TriangleIndices.Count > 0
            ? selection.TriangleIndices
            : Enumerable.Range(0, source.Triangles.Count)
                .Where(index =>
                {
                    Triangle triangle = source.Triangles[index];
                    return selectedVertices.Contains(triangle.A) || selectedVertices.Contains(triangle.B) || selectedVertices.Contains(triangle.C);
                })
                .ToList();

        foreach (int triangleIndex in selectedTriangleIndices)
        {
            if (triangleIndex < 0 || triangleIndex >= source.Triangles.Count)
                continue;

            Triangle triangle = source.Triangles[triangleIndex];
            if (!vertexMap.ContainsKey(triangle.A) || !vertexMap.ContainsKey(triangle.B) || !vertexMap.ContainsKey(triangle.C))
                continue;

            extracted.Triangles.Add(new Triangle
            {
                A = vertexMap[triangle.A],
                B = vertexMap[triangle.B],
                C = vertexMap[triangle.C],
                MaterialSlotIndex = triangle.MaterialSlotIndex,
                SectionIndex = triangle.SectionIndex,
                LodIndex = triangle.LodIndex
            });
        }

        extracted.LODGroups.Add(new LODGroup
        {
            LevelIndex = 0,
            ScreenSize = 1.0f,
            TriangleIndices = Enumerable.Range(0, extracted.Triangles.Count).ToList()
        });

        extracted.Sockets.AddRange(source.Sockets
            .Where(socket => string.IsNullOrWhiteSpace(socket.BoneName) || usedBones.Contains(socket.BoneIndex) || source.Bones.Any(bone => string.Equals(bone.Name, socket.BoneName, StringComparison.OrdinalIgnoreCase) && usedBones.Contains(source.Bones.IndexOf(bone))))
            .Select(socket => socket.Clone()));

        extracted.RecalculateBounds();
        return extracted;
    }

    private static List<BoneWeight> NormalizeBoneWeights(List<BoneWeight> weights, Mesh source)
    {
        List<BoneWeight> normalized = weights
            .Where(weight => weight.BoneIndex >= 0 && weight.BoneIndex < source.Bones.Count && weight.Weight > 0.0f)
            .OrderByDescending(weight => weight.Weight)
            .Take(4)
            .Select(weight => weight.Clone())
            .ToList();

        float total = normalized.Sum(weight => weight.Weight);
        if (total > 0.0f)
        {
            for (int index = 0; index < normalized.Count; index++)
                normalized[index].Weight /= total;
        }

        return normalized;
    }
}

