using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class SectionCalibrator
{
    public SectionCalibrationReport Calibrate(Mesh mesh, string meshKey)
    {
        List<SectionCalibrationEntry> entries = [];
        foreach (IGrouping<int, Triangle> sectionGroup in mesh.Triangles
            .Where(triangle => triangle.SectionIndex >= 0)
            .GroupBy(triangle => triangle.SectionIndex))
        {
            HashSet<int> vertexIndices = [];
            Dictionary<int, float> boneWeights = [];
            Vector3 centroid = Vector3.Zero;
            BoundingBox bounds = BoundingBox.Empty;

            foreach (Triangle triangle in sectionGroup)
            {
                CollectVertex(mesh, triangle.A, vertexIndices, ref centroid, ref bounds, boneWeights);
                CollectVertex(mesh, triangle.B, vertexIndices, ref centroid, ref bounds, boneWeights);
                CollectVertex(mesh, triangle.C, vertexIndices, ref centroid, ref bounds, boneWeights);
            }

            int vertexCount = Math.Max(vertexIndices.Count, 1);
            centroid /= vertexCount;
            int representativeBoneIndex = boneWeights.Count == 0
                ? -1
                : boneWeights.OrderByDescending(item => item.Value).First().Key;
            string representativeBoneName = representativeBoneIndex >= 0 && representativeBoneIndex < mesh.Bones.Count
                ? mesh.Bones[representativeBoneIndex].Name
                : string.Empty;

            entries.Add(new SectionCalibrationEntry
            {
                SectionIndex = sectionGroup.Key,
                RepresentativeBoneIndex = representativeBoneIndex,
                RepresentativeBoneName = representativeBoneName,
                BoneGroup = ClassifyBoneGroup(representativeBoneName),
                TriangleCount = sectionGroup.Count(),
                VertexCount = vertexIndices.Count,
                Centroid = centroid,
                Bounds = bounds
            });
        }

        return new SectionCalibrationReport
        {
            MeshKey = meshKey,
            MeshName = mesh.Name,
            CalibratedAt = DateTimeOffset.UtcNow,
            Entries = entries.OrderBy(entry => entry.SectionIndex).ToList()
        };
    }

    private static void CollectVertex(
        Mesh mesh,
        int vertexIndex,
        HashSet<int> vertexIndices,
        ref Vector3 centroid,
        ref BoundingBox bounds,
        IDictionary<int, float> boneWeights)
    {
        if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count || !vertexIndices.Add(vertexIndex))
            return;

        Vertex vertex = mesh.Vertices[vertexIndex];
        centroid += vertex.Position;
        bounds.Include(vertex.Position);
        foreach (BoneWeight weight in vertex.Weights)
        {
            if (weight.BoneIndex < 0)
                continue;

            boneWeights.TryGetValue(weight.BoneIndex, out float current);
            boneWeights[weight.BoneIndex] = current + Math.Max(0.0f, weight.Weight);
        }
    }

    private static string ClassifyBoneGroup(string boneName)
    {
        string normalized = boneName.Trim().ToLowerInvariant();
        if (normalized.Contains("head") || normalized.Contains("eye") || normalized.Contains("jaw") || normalized.Contains("face"))
            return "Head";
        if (normalized.Contains("neck"))
            return "Neck";
        if (normalized.Contains("spine") || normalized.Contains("torso") || normalized.Contains("chest") || normalized.Contains("abdomen") || normalized.Contains("pelvis") || normalized.Contains("root"))
            return "Torso";
        if (normalized.Contains("clavicle") || normalized.Contains("shoulder") || normalized.Contains("upperarm") || normalized.Contains("lowerarm") || normalized.Contains("arm") || normalized.Contains("elbow") || normalized.Contains("wrist") || normalized.Contains("hand") || normalized.Contains("finger") || normalized.Contains("thumb") || normalized.Contains("palm"))
            return "Arm";
        if (normalized.Contains("thigh") || normalized.Contains("calf") || normalized.Contains("shin") || normalized.Contains("knee") || normalized.Contains("leg") || normalized.Contains("ankle") || normalized.Contains("foot") || normalized.Contains("toe"))
            return "Leg";
        if (normalized.Contains("cape") || normalized.Contains("cloth") || normalized.Contains("skirt") || normalized.Contains("tail") || normalized.Contains("belt") || normalized.Contains("armor"))
            return "Accessory";
        return "Other";
    }
}

