using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class RegionSelector
{
    public RegionSelectionResult SelectByRay(Mesh mesh, Vector3 origin, Vector3 direction)
    {
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        float bestDistance = float.MaxValue;
        int bestTriangle = -1;
        Vector3 bestPoint = Vector3.Zero;

        for (int index = 0; index < mesh.Triangles.Count; index++)
        {
            Triangle triangle = mesh.Triangles[index];
            if (!IsTriangleValid(mesh, triangle))
                continue;

            if (TryIntersectTriangle(origin, normalizedDirection, mesh.Vertices[triangle.A].Position, mesh.Vertices[triangle.B].Position, mesh.Vertices[triangle.C].Position, out float distance, out Vector3 hitPoint) && distance < bestDistance)
            {
                bestDistance = distance;
                bestTriangle = index;
                bestPoint = hitPoint;
            }
        }

        if (bestTriangle < 0)
        {
            return new RegionSelectionResult
            {
                Mode = "Raycast",
                TriangleIndex = -1,
                HitPoint = Vector3.Zero
            };
        }

        Triangle selectedTriangle = mesh.Triangles[bestTriangle];
        int dominantBone = DetermineDominantBone(mesh, selectedTriangle);
        return BuildSelection(mesh, "Raycast", bestTriangle, dominantBone, selectedTriangle.SectionIndex, bestPoint);
    }

    public RegionSelectionResult SelectByBone(Mesh mesh, string boneName)
    {
        int boneIndex = mesh.Bones.FindIndex(bone => string.Equals(bone.Name, boneName, StringComparison.OrdinalIgnoreCase));
        if (boneIndex < 0)
        {
            return new RegionSelectionResult
            {
                Mode = "Bone",
                BoneName = boneName,
                BoneIndex = -1
            };
        }

        return BuildBoneSelection(mesh, boneIndex, boneName);
    }

    public RegionSelectionResult SelectBySection(Mesh mesh, int sectionIndex)
    {
        List<int> triangleIndices = [];
        HashSet<int> vertexIndices = [];

        for (int index = 0; index < mesh.Triangles.Count; index++)
        {
            Triangle triangle = mesh.Triangles[index];
            if (triangle.SectionIndex != sectionIndex)
                continue;

            triangleIndices.Add(index);
            vertexIndices.Add(triangle.A);
            vertexIndices.Add(triangle.B);
            vertexIndices.Add(triangle.C);
        }

        int dominantBone = DetermineDominantBone(mesh, triangleIndices);
        string boneName = dominantBone >= 0 && dominantBone < mesh.Bones.Count ? mesh.Bones[dominantBone].Name : string.Empty;
        return new RegionSelectionResult
        {
            Mode = "Section",
            SectionIndex = sectionIndex,
            BoneIndex = dominantBone,
            BoneName = boneName,
            TriangleIndices = triangleIndices,
            VertexIndices = vertexIndices.OrderBy(value => value).ToList(),
            TriangleIndex = triangleIndices.FirstOrDefault(-1)
        };
    }

    public RegionSelectionResult SelectByTriangle(Mesh mesh, int triangleIndex, Vector3 hitPoint)
    {
        if (triangleIndex < 0 || triangleIndex >= mesh.Triangles.Count)
        {
            return new RegionSelectionResult
            {
                Mode = "Triangle",
                TriangleIndex = -1,
                HitPoint = hitPoint
            };
        }

        Triangle triangle = mesh.Triangles[triangleIndex];
        int dominantBone = DetermineDominantBone(mesh, triangle);
        return BuildSelection(mesh, "Triangle", triangleIndex, dominantBone, triangle.SectionIndex, hitPoint);
    }

    public int DetermineDominantBone(Mesh mesh, Triangle triangle)
    {
        return DetermineDominantBone(mesh, [triangle]);
    }

    public int DetermineDominantBone(Mesh mesh, IEnumerable<int> triangleIndices)
    {
        List<Triangle> triangles = triangleIndices
            .Where(index => index >= 0 && index < mesh.Triangles.Count)
            .Select(index => mesh.Triangles[index])
            .ToList();

        return DetermineDominantBone(mesh, triangles);
    }

    private static int DetermineDominantBone(Mesh mesh, IReadOnlyList<Triangle> triangles)
    {
        Dictionary<int, float> totals = [];
        foreach (Triangle triangle in triangles)
        {
            AccumulateWeights(mesh, triangle.A, totals);
            AccumulateWeights(mesh, triangle.B, totals);
            AccumulateWeights(mesh, triangle.C, totals);
        }

        return totals.Count == 0 ? -1 : totals.OrderByDescending(entry => entry.Value).First().Key;
    }

    private static void AccumulateWeights(Mesh mesh, int vertexIndex, IDictionary<int, float> totals)
    {
        if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            return;

        foreach (BoneWeight weight in mesh.Vertices[vertexIndex].Weights)
        {
            if (weight.BoneIndex < 0)
                continue;

            totals.TryGetValue(weight.BoneIndex, out float current);
            totals[weight.BoneIndex] = current + weight.Weight;
        }
    }

    private RegionSelectionResult BuildBoneSelection(Mesh mesh, int boneIndex, string boneName)
    {
        HashSet<int> vertexIndices = [];
        for (int index = 0; index < mesh.Vertices.Count; index++)
        {
            if (mesh.Vertices[index].Weights.Any(weight => weight.BoneIndex == boneIndex && weight.Weight > 0.05f))
                vertexIndices.Add(index);
        }

        List<int> triangleIndices = [];
        foreach (int triangleIndex in Enumerable.Range(0, mesh.Triangles.Count))
        {
            Triangle triangle = mesh.Triangles[triangleIndex];
            if (vertexIndices.Contains(triangle.A) || vertexIndices.Contains(triangle.B) || vertexIndices.Contains(triangle.C))
                triangleIndices.Add(triangleIndex);
        }

        return new RegionSelectionResult
        {
            Mode = "Bone",
            BoneIndex = boneIndex,
            BoneName = boneName,
            TriangleIndices = triangleIndices,
            VertexIndices = vertexIndices.OrderBy(value => value).ToList(),
            TriangleIndex = triangleIndices.FirstOrDefault(-1),
            SectionIndex = triangleIndices.Count > 0 ? mesh.Triangles[triangleIndices[0]].SectionIndex : -1
        };
    }

    private static RegionSelectionResult BuildSelection(Mesh mesh, string mode, int triangleIndex, int dominantBone, int sectionIndex, Vector3 hitPoint)
    {
        List<int> triangleIndices = triangleIndex >= 0 ? [triangleIndex] : [];
        HashSet<int> vertexIndices = [];

        if (triangleIndex >= 0)
        {
            if (sectionIndex >= 0 && !string.Equals(mode, "Bone", StringComparison.OrdinalIgnoreCase))
            {
                triangleIndices = mesh.Triangles
                    .Select((triangleItem, index) => (triangleItem, index))
                    .Where(item => item.triangleItem.SectionIndex == sectionIndex)
                    .Select(item => item.index)
                    .ToList();
                }
            else if (dominantBone >= 0)
            {
                for (int index = 0; index < mesh.Vertices.Count; index++)
                {
                    if (mesh.Vertices[index].Weights.Any(weight => weight.BoneIndex == dominantBone && weight.Weight > 0.05f))
                        vertexIndices.Add(index);
                }

                triangleIndices = mesh.Triangles
                    .Select((triangleItem, index) => (triangleItem, index))
                    .Where(item => vertexIndices.Contains(item.triangleItem.A) || vertexIndices.Contains(item.triangleItem.B) || vertexIndices.Contains(item.triangleItem.C))
                    .Select(item => item.index)
                    .ToList();
            }

            if (vertexIndices.Count == 0)
            {
                foreach (int selectedTriangleIndex in triangleIndices)
                {
                    if (selectedTriangleIndex < 0 || selectedTriangleIndex >= mesh.Triangles.Count)
                        continue;

                    Triangle selected = mesh.Triangles[selectedTriangleIndex];
                    vertexIndices.Add(selected.A);
                    vertexIndices.Add(selected.B);
                    vertexIndices.Add(selected.C);
                }
            }
        }

        string boneName = dominantBone >= 0 && dominantBone < mesh.Bones.Count ? mesh.Bones[dominantBone].Name : string.Empty;
        return new RegionSelectionResult
        {
            Mode = mode,
            TriangleIndex = triangleIndex,
            BoneIndex = dominantBone,
            BoneName = boneName,
            SectionIndex = sectionIndex,
            HitPoint = hitPoint,
            TriangleIndices = triangleIndices,
            VertexIndices = vertexIndices.OrderBy(value => value).ToList()
        };
    }

    private static bool TryIntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 hitPoint)
    {
        distance = 0.0f;
        hitPoint = Vector3.Zero;
        const float epsilon = 0.000001f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 h = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, h);

        if (det > -epsilon && det < epsilon)
            return false;

        float invDet = 1.0f / det;
        Vector3 s = origin - a;
        float u = invDet * Vector3.Dot(s, h);
        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = invDet * Vector3.Dot(direction, q);
        if (v < 0.0f || u + v > 1.0f)
            return false;

        float t = invDet * Vector3.Dot(edge2, q);
        if (t <= epsilon)
            return false;

        distance = t;
        hitPoint = origin + direction * t;
        return true;
    }

    private static bool IsTriangleValid(Mesh mesh, Triangle triangle)
    {
        return triangle.A >= 0 && triangle.B >= 0 && triangle.C >= 0
            && triangle.A < mesh.Vertices.Count
            && triangle.B < mesh.Vertices.Count
            && triangle.C < mesh.Vertices.Count;
    }
}

public sealed class RegionSelectionResult
{
    public string Mode { get; set; } = string.Empty;

    public int TriangleIndex { get; set; } = -1;

    public int BoneIndex { get; set; } = -1;

    public string BoneName { get; set; } = string.Empty;

    public int SectionIndex { get; set; } = -1;

    public Vector3 HitPoint { get; set; } = Vector3.Zero;

    public List<int> TriangleIndices { get; set; } = [];

    public List<int> VertexIndices { get; set; } = [];
}

