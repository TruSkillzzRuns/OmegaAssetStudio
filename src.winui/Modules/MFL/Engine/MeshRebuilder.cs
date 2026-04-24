using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class MeshRebuilder
{
    public MeshRebuildReport LastReport { get; private set; } = new();

    public Mesh Rebuild(Mesh mesh)
    {
        Mesh rebuilt = mesh.Clone();
        int seamEdges = RebuildUvSeams(rebuilt);
        NormalizeBoneWeights(rebuilt);
        RebuildNormals(rebuilt);
        RebuildTangents(rebuilt);
        RebuildLods(rebuilt);
        RebuildSockets(rebuilt);
        EnsureMaterials(rebuilt);
        rebuilt.RecalculateBounds();

        LastReport = new MeshRebuildReport
        {
            SeamEdges = seamEdges,
            VertexCount = rebuilt.Vertices.Count,
            TriangleCount = rebuilt.Triangles.Count,
            BoneCount = rebuilt.Bones.Count,
            SocketCount = rebuilt.Sockets.Count,
            LodCount = rebuilt.LODGroups.Count
        };

        return rebuilt;
    }

    private static void NormalizeBoneWeights(Mesh mesh)
    {
        foreach (Vertex vertex in mesh.Vertices)
        {
            vertex.Weights = vertex.Weights
                .Where(weight => weight.Weight > 0.0f)
                .OrderByDescending(weight => weight.Weight)
                .Take(4)
                .Select(weight => weight.Clone())
                .ToList();

            float total = vertex.Weights.Sum(weight => weight.Weight);
            if (total <= 0.0f)
            {
                if (mesh.Bones.Count > 0)
                    vertex.Weights = [new BoneWeight { BoneIndex = 0, BoneName = mesh.Bones[0].Name, Weight = 1.0f }];

                continue;
            }

            for (int index = 0; index < vertex.Weights.Count; index++)
                vertex.Weights[index].Weight /= total;
        }
    }

    private static void RebuildNormals(Mesh mesh)
    {
        foreach (Vertex vertex in mesh.Vertices)
            vertex.Normal = Vector3.Zero;

        foreach (Triangle triangle in mesh.Triangles)
        {
            if (!IsTriangleValid(mesh, triangle))
                continue;

            Vector3 a = mesh.Vertices[triangle.A].Position;
            Vector3 b = mesh.Vertices[triangle.B].Position;
            Vector3 c = mesh.Vertices[triangle.C].Position;
            Vector3 faceNormal = Vector3.Cross(b - a, c - a);
            if (faceNormal == Vector3.Zero)
                continue;

            mesh.Vertices[triangle.A].Normal += faceNormal;
            mesh.Vertices[triangle.B].Normal += faceNormal;
            mesh.Vertices[triangle.C].Normal += faceNormal;
        }

        foreach (Vertex vertex in mesh.Vertices)
            vertex.Normal = NormalizeOrFallback(vertex.Normal, Vector3.UnitY);
    }

    private static void RebuildTangents(Mesh mesh)
    {
        foreach (Vertex vertex in mesh.Vertices)
        {
            vertex.Tangent = Vector3.Zero;
            vertex.Bitangent = Vector3.Zero;
        }

        if (mesh.UVSets.Count == 0 || mesh.UVSets[0].Coordinates.Count < mesh.Vertices.Count)
        {
            foreach (Vertex vertex in mesh.Vertices)
            {
                vertex.Tangent = Vector3.UnitX;
                vertex.Bitangent = Vector3.UnitZ;
            }

            return;
        }

        foreach (Triangle triangle in mesh.Triangles)
        {
            if (!IsTriangleValid(mesh, triangle))
                continue;

            Vector3 p0 = mesh.Vertices[triangle.A].Position;
            Vector3 p1 = mesh.Vertices[triangle.B].Position;
            Vector3 p2 = mesh.Vertices[triangle.C].Position;
            Vector2 uv0 = mesh.UVSets[0].Coordinates[triangle.A];
            Vector2 uv1 = mesh.UVSets[0].Coordinates[triangle.B];
            Vector2 uv2 = mesh.UVSets[0].Coordinates[triangle.C];

            Vector3 edge1 = p1 - p0;
            Vector3 edge2 = p2 - p0;
            Vector2 delta1 = uv1 - uv0;
            Vector2 delta2 = uv2 - uv0;
            float determinant = delta1.X * delta2.Y - delta2.X * delta1.Y;
            if (MathF.Abs(determinant) < 0.000001f)
                continue;

            float inverse = 1.0f / determinant;
            Vector3 tangent = new(
                inverse * (delta2.Y * edge1.X - delta1.Y * edge2.X),
                inverse * (delta2.Y * edge1.Y - delta1.Y * edge2.Y),
                inverse * (delta2.Y * edge1.Z - delta1.Y * edge2.Z));

            Vector3 bitangent = new(
                inverse * (-delta2.X * edge1.X + delta1.X * edge2.X),
                inverse * (-delta2.X * edge1.Y + delta1.X * edge2.Y),
                inverse * (-delta2.X * edge1.Z + delta1.X * edge2.Z));

            mesh.Vertices[triangle.A].Tangent += tangent;
            mesh.Vertices[triangle.B].Tangent += tangent;
            mesh.Vertices[triangle.C].Tangent += tangent;
            mesh.Vertices[triangle.A].Bitangent += bitangent;
            mesh.Vertices[triangle.B].Bitangent += bitangent;
            mesh.Vertices[triangle.C].Bitangent += bitangent;
        }

        foreach (Vertex vertex in mesh.Vertices)
        {
            Vector3 normal = NormalizeOrFallback(vertex.Normal, Vector3.UnitY);
            Vector3 tangent = vertex.Tangent - normal * Vector3.Dot(normal, vertex.Tangent);
            tangent = NormalizeOrFallback(tangent, CreateOrthogonalVector(normal));
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            if (Vector3.Dot(bitangent, vertex.Bitangent) < 0.0f)
                bitangent = -bitangent;

            vertex.Normal = normal;
            vertex.Tangent = tangent;
            vertex.Bitangent = NormalizeOrFallback(bitangent, Vector3.UnitZ);
        }
    }

    private static int RebuildUvSeams(Mesh mesh)
    {
        if (mesh.UVSets.Count == 0)
            return 0;

        HashSet<(int, int)> seamEdges = [];
        Dictionary<(int, int), List<(Vector2 A, Vector2 B)>> edgeUvs = [];

        foreach (Triangle triangle in mesh.Triangles)
        {
            TrackEdge(edgeUvs, (triangle.A, triangle.B), mesh, triangle.A, triangle.B);
            TrackEdge(edgeUvs, (triangle.B, triangle.C), mesh, triangle.B, triangle.C);
            TrackEdge(edgeUvs, (triangle.C, triangle.A), mesh, triangle.C, triangle.A);
        }

        foreach (var entry in edgeUvs)
        {
            if (entry.Value.Count > 1)
            {
                Vector2 firstA = entry.Value[0].A;
                Vector2 firstB = entry.Value[0].B;
                foreach ((Vector2 A, Vector2 B) pair in entry.Value.Skip(1))
                {
                    if (pair.A != firstA || pair.B != firstB)
                    {
                        seamEdges.Add(entry.Key);
                        break;
                    }
                }
            }
        }

        return seamEdges.Count;
    }

    private static void TrackEdge(IDictionary<(int, int), List<(Vector2 A, Vector2 B)>> edgeUvs, (int, int) edge, Mesh mesh, int aIndex, int bIndex)
    {
        (int, int) key = edge.Item1 < edge.Item2 ? edge : (edge.Item2, edge.Item1);
        if (!edgeUvs.TryGetValue(key, out List<(Vector2 A, Vector2 B)>? list))
        {
            list = [];
            edgeUvs[key] = list;
        }

        Vector2 uvA = mesh.UVSets[0].Coordinates[aIndex];
        Vector2 uvB = mesh.UVSets[0].Coordinates[bIndex];
        list.Add((uvA, uvB));
    }

    private static void RebuildLods(Mesh mesh)
    {
        if (mesh.LODGroups.Count == 0)
        {
            mesh.LODGroups.Add(new LODGroup
            {
                LevelIndex = 0,
                ScreenSize = 1.0f,
                TriangleIndices = Enumerable.Range(0, mesh.Triangles.Count).ToList()
            });
            return;
        }

        foreach (LODGroup lod in mesh.LODGroups)
        {
            lod.TriangleIndices = lod.TriangleIndices
                .Where(index => index >= 0 && index < mesh.Triangles.Count)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
        }
    }

    private static void RebuildSockets(Mesh mesh)
    {
        if (mesh.Sockets.Count == 0 && mesh.Bones.Count > 0)
        {
            mesh.Sockets.Add(new Socket
            {
                Name = "RootSocket",
                BoneIndex = 0,
                BoneName = mesh.Bones[0].Name,
                Position = mesh.Bones[0].BindPosition,
                Rotation = mesh.Bones[0].BindRotation
            });
            return;
        }

        foreach (Socket socket in mesh.Sockets)
        {
            if (string.IsNullOrWhiteSpace(socket.BoneName) && socket.BoneIndex >= 0 && socket.BoneIndex < mesh.Bones.Count)
                socket.BoneName = mesh.Bones[socket.BoneIndex].Name;

            if (socket.BoneIndex < 0 || socket.BoneIndex >= mesh.Bones.Count)
            {
                int mappedIndex = mesh.Bones.FindIndex(bone => string.Equals(bone.Name, socket.BoneName, StringComparison.OrdinalIgnoreCase));
                if (mappedIndex < 0 && mesh.Bones.Count > 0)
                    mappedIndex = 0;

                socket.BoneIndex = mappedIndex;
                if (mappedIndex >= 0 && mappedIndex < mesh.Bones.Count && string.IsNullOrWhiteSpace(socket.BoneName))
                    socket.BoneName = mesh.Bones[mappedIndex].Name;
            }
        }
    }

    private static void EnsureMaterials(Mesh mesh)
    {
        if (mesh.MaterialSlots.Count == 0)
        {
            mesh.MaterialSlots.Add(new MaterialSlot
            {
                Index = 0,
                Name = "DefaultMaterial",
                MaterialPath = string.Empty
            });
        }

        for (int index = 0; index < mesh.Triangles.Count; index++)
        {
            Triangle triangle = mesh.Triangles[index];
            if (triangle.MaterialSlotIndex < 0 || triangle.MaterialSlotIndex >= mesh.MaterialSlots.Count)
                triangle.MaterialSlotIndex = 0;
        }
    }

    private static bool IsTriangleValid(Mesh mesh, Triangle triangle)
    {
        return triangle.A >= 0 && triangle.B >= 0 && triangle.C >= 0
            && triangle.A < mesh.Vertices.Count
            && triangle.B < mesh.Vertices.Count
            && triangle.C < mesh.Vertices.Count;
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        if (value == Vector3.Zero)
            return fallback;

        Vector3 normalized = Vector3.Normalize(value);
        return float.IsNaN(normalized.X) || float.IsNaN(normalized.Y) || float.IsNaN(normalized.Z) ? fallback : normalized;
    }

    private static Vector3 CreateOrthogonalVector(Vector3 normal)
    {
        Vector3 axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Cross(axis, normal);
        return tangent == Vector3.Zero ? Vector3.UnitX : Vector3.Normalize(tangent);
    }
}

public sealed class MeshRebuildReport
{
    public int SeamEdges { get; set; }

    public int VertexCount { get; set; }

    public int TriangleCount { get; set; }

    public int BoneCount { get; set; }

    public int SocketCount { get; set; }

    public int LodCount { get; set; }
}

