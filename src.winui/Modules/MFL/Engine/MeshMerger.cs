using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class MeshMerger
{
    public Mesh Merge(Mesh target, Mesh source)
    {
        Mesh merged = target.Clone();
        int vertexOffset = merged.Vertices.Count;
        int triangleOffset = merged.Triangles.Count;

        Dictionary<string, int> boneMap = merged.Bones
            .Select((bone, index) => (bone, index))
            .ToDictionary(item => item.bone.Name, item => item.index, StringComparer.OrdinalIgnoreCase);

        foreach (Bone bone in source.Bones)
        {
            if (!boneMap.ContainsKey(bone.Name))
            {
                boneMap[bone.Name] = merged.Bones.Count;
                merged.Bones.Add(bone.Clone());
            }
        }

        Dictionary<string, int> materialMap = merged.MaterialSlots
            .Select((slot, index) => (slot, index))
            .ToDictionary(item => item.slot.Name, item => item.index, StringComparer.OrdinalIgnoreCase);

        foreach (MaterialSlot slot in source.MaterialSlots)
        {
            if (!materialMap.ContainsKey(slot.Name))
            {
                materialMap[slot.Name] = merged.MaterialSlots.Count;
                merged.MaterialSlots.Add(slot.Clone());
            }
        }

        EnsureUvSlots(merged, source);

        foreach (Vertex vertex in source.Vertices)
        {
            Vertex clone = vertex.Clone();
            clone.Weights = clone.Weights
                .Select(weight =>
                {
                    string boneName = ResolveBoneName(source, weight);
                    int boneIndex = boneMap.TryGetValue(boneName, out int mappedIndex) ? mappedIndex : -1;
                    return new BoneWeight
                    {
                        BoneIndex = boneIndex,
                        BoneName = boneName,
                        Weight = weight.Weight
                    };
                })
                .Where(weight => weight.BoneIndex >= 0)
                .ToList();

            merged.Vertices.Add(clone);
        }

        foreach (Triangle triangle in source.Triangles)
        {
            merged.Triangles.Add(new Triangle
            {
                A = triangle.A + vertexOffset,
                B = triangle.B + vertexOffset,
                C = triangle.C + vertexOffset,
                MaterialSlotIndex = MapMaterialIndex(source, materialMap, triangle.MaterialSlotIndex),
                SectionIndex = triangle.SectionIndex,
                LodIndex = triangle.LodIndex
            });
        }

        MergeUvCoordinates(merged, source, vertexOffset);
        MergeLods(merged, source, triangleOffset);
        MergeSockets(merged, source, boneMap);
        merged.RecalculateBounds();
        return merged;
    }

    private static void EnsureUvSlots(Mesh target, Mesh source)
    {
        int maxChannels = Math.Max(target.UVSets.Count, source.UVSets.Count);
        for (int channel = 0; channel < maxChannels; channel++)
        {
            if (channel >= target.UVSets.Count)
            {
                target.UVSets.Add(new UVSet
                {
                    ChannelIndex = channel,
                    Name = $"UV{channel}",
                    Coordinates = Enumerable.Repeat(Vector2.Zero, target.Vertices.Count).ToList()
                });
            }
        }
    }

    private static void MergeUvCoordinates(Mesh merged, Mesh source, int vertexOffset)
    {
        int maxChannels = Math.Max(merged.UVSets.Count, source.UVSets.Count);
        for (int channel = 0; channel < maxChannels; channel++)
        {
            if (channel >= merged.UVSets.Count)
            {
                merged.UVSets.Add(new UVSet
                {
                    ChannelIndex = channel,
                    Name = $"UV{channel}",
                    Coordinates = Enumerable.Repeat(Vector2.Zero, vertexOffset).ToList()
                });
            }

            UVSet mergedSet = merged.UVSets[channel];
            while (mergedSet.Coordinates.Count < vertexOffset)
                mergedSet.Coordinates.Add(Vector2.Zero);

            foreach (Vertex vertex in source.Vertices)
            {
                Vector2 uv = channel < vertex.UVs.Count ? vertex.UVs[channel] : Vector2.Zero;
                mergedSet.Coordinates.Add(uv);
            }
        }
    }

    private static void MergeLods(Mesh merged, Mesh source, int sourceTriangleOffset)
    {
        if (source.LODGroups.Count == 0)
        {
            if (merged.LODGroups.Count == 0)
            {
                merged.LODGroups.Add(new LODGroup
                {
                    LevelIndex = 0,
                    ScreenSize = 1.0f,
                    TriangleIndices = Enumerable.Range(0, merged.Triangles.Count).ToList()
                });
            }

            return;
        }

        foreach (LODGroup sourceGroup in source.LODGroups)
        {
            LODGroup clone = sourceGroup.Clone();
            clone.TriangleIndices = clone.TriangleIndices
                .Select(index => index + sourceTriangleOffset)
                .Where(index => index >= sourceTriangleOffset && index < sourceTriangleOffset + source.Triangles.Count)
                .Distinct()
                .OrderBy(index => index)
                .ToList();

            if (clone.TriangleIndices.Count == 0)
                continue;

            LODGroup? existing = merged.LODGroups.FirstOrDefault(group => group.LevelIndex == clone.LevelIndex);
            if (existing is null)
            {
                merged.LODGroups.Add(clone);
                continue;
            }

            existing.ScreenSize = MathF.Max(existing.ScreenSize, clone.ScreenSize);
            existing.TriangleIndices = existing.TriangleIndices
                .Concat(clone.TriangleIndices)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        if (merged.LODGroups.Count == 0)
        {
            merged.LODGroups.Add(new LODGroup
            {
                LevelIndex = 0,
                ScreenSize = 1.0f,
                TriangleIndices = Enumerable.Range(0, merged.Triangles.Count).ToList()
            });
        }
    }

    private static void MergeSockets(Mesh merged, Mesh source, IDictionary<string, int> boneMap)
    {
        foreach (Socket socket in source.Sockets)
        {
            if (merged.Sockets.Any(existing => string.Equals(existing.Name, socket.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            string boneName = socket.BoneName;
            if (string.IsNullOrWhiteSpace(boneName) && socket.BoneIndex >= 0 && socket.BoneIndex < source.Bones.Count)
                boneName = source.Bones[socket.BoneIndex].Name;

            int boneIndex = boneMap.TryGetValue(boneName, out int mappedIndex) ? mappedIndex : -1;
            merged.Sockets.Add(new Socket
            {
                Name = socket.Name,
                BoneName = boneName,
                BoneIndex = boneIndex,
                Position = socket.Position,
                Rotation = socket.Rotation
            });
        }
    }

    private static string ResolveBoneName(Mesh source, BoneWeight weight)
    {
        if (!string.IsNullOrWhiteSpace(weight.BoneName))
            return weight.BoneName;

        if (weight.BoneIndex >= 0 && weight.BoneIndex < source.Bones.Count)
            return source.Bones[weight.BoneIndex].Name;

        return string.Empty;
    }

    private static int MapMaterialIndex(Mesh source, IDictionary<string, int> materialMap, int materialIndex)
    {
        if (materialIndex < 0 || materialIndex >= source.MaterialSlots.Count)
            return materialIndex;

        string materialName = source.MaterialSlots[materialIndex].Name;
        return materialMap.TryGetValue(materialName, out int mappedIndex) ? mappedIndex : materialIndex;
    }
}

