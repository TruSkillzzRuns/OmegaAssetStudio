using System.Text;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class MeshExporter
{
    public string ExportPsk(Mesh mesh, string path)
    {
        return ExportStructuredMesh(mesh, path, "PSK");
    }

    public string ExportFbx(Mesh mesh, string path)
    {
        return ExportStructuredMesh(mesh, path, "FBX");
    }

    private static string ExportStructuredMesh(Mesh mesh, string path, string format)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        StringBuilder builder = new();
        builder.AppendLine($"Mesh Fusion Lab {format} Structure");
        builder.AppendLine($"Format: {format}");
        builder.AppendLine("Version: 1");
        builder.AppendLine($"Name: {mesh.Name}");
        builder.AppendLine($"Source: {mesh.SourcePath}");
        builder.AppendLine($"Vertices: {mesh.Vertices.Count}");
        builder.AppendLine($"Triangles: {mesh.Triangles.Count}");
        builder.AppendLine($"Bones: {mesh.Bones.Count}");
        builder.AppendLine($"RootBone: {(mesh.Bones.Count > 0 ? mesh.Bones[0].Name : string.Empty)}");
        builder.AppendLine($"Materials: {mesh.MaterialSlots.Count}");
        builder.AppendLine($"UVSets: {mesh.UVSets.Count}");
        builder.AppendLine($"LODs: {mesh.LODGroups.Count}");
        builder.AppendLine($"Sockets: {mesh.Sockets.Count}");
        builder.AppendLine($"Influences: {mesh.Vertices.Sum(vertex => vertex.Weights.Count)}");
        builder.AppendLine($"Bounds: Min={FormatVector(mesh.Bounds.Min)} Max={FormatVector(mesh.Bounds.Max)}");
        builder.AppendLine();
        builder.AppendLine("[Bones]");
        for (int index = 0; index < mesh.Bones.Count; index++)
        {
            Bone bone = mesh.Bones[index];
            builder.AppendLine($"{index}: {bone.Name} Parent={bone.ParentIndex} Pos={FormatVector(bone.BindPosition)} Rot={FormatQuaternion(bone.BindRotation)}");
        }

        builder.AppendLine();
        builder.AppendLine("[Vertices]");
        for (int index = 0; index < mesh.Vertices.Count; index++)
        {
            Vertex vertex = mesh.Vertices[index];
            builder.AppendLine($"{index}: P={FormatVector(vertex.Position)} N={FormatVector(vertex.Normal)} T={FormatVector(vertex.Tangent)} B={FormatVector(vertex.Bitangent)} Weights={FormatWeights(vertex.Weights)}");
        }

        builder.AppendLine();
        builder.AppendLine("[Triangles]");
        for (int index = 0; index < mesh.Triangles.Count; index++)
        {
            Triangle triangle = mesh.Triangles[index];
            builder.AppendLine($"{index}: {triangle.A},{triangle.B},{triangle.C} Material={triangle.MaterialSlotIndex} Section={triangle.SectionIndex} LOD={triangle.LodIndex}");
        }

        builder.AppendLine();
        builder.AppendLine("[Materials]");
        foreach (MaterialSlot slot in mesh.MaterialSlots)
            builder.AppendLine($"{slot.Index}: {slot.Name} {slot.MaterialPath}");

        builder.AppendLine();
        builder.AppendLine("[UVSets]");
        foreach (UVSet uvSet in mesh.UVSets)
        {
            builder.AppendLine($"{uvSet.ChannelIndex}: {uvSet.Name}");
            for (int index = 0; index < uvSet.Coordinates.Count; index++)
                builder.AppendLine($"{index}: {FormatVector(uvSet.Coordinates[index])}");
        }

        builder.AppendLine();
        builder.AppendLine("[LODs]");
        foreach (LODGroup lod in mesh.LODGroups)
            builder.AppendLine($"{lod.LevelIndex}: Screen={lod.ScreenSize:0.###} Triangles={string.Join(",", lod.TriangleIndices)}");

        builder.AppendLine();
        builder.AppendLine("[Sockets]");
        foreach (Socket socket in mesh.Sockets)
            builder.AppendLine($"{socket.Name}: Bone={socket.BoneName} Index={socket.BoneIndex} Pos={FormatVector(socket.Position)} Rot={FormatQuaternion(socket.Rotation)}");

        string output = builder.ToString();
        File.WriteAllText(path, output, Encoding.UTF8);
        string companionPath = Path.ChangeExtension(path, ".mflmesh");
        File.WriteAllText(companionPath, output, Encoding.UTF8);
        return path;
    }

    private static string FormatVector(System.Numerics.Vector3 value) => $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";

    private static string FormatVector(System.Numerics.Vector2 value) => $"({value.X:0.###}, {value.Y:0.###})";

    private static string FormatQuaternion(System.Numerics.Quaternion value) => $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}, {value.W:0.###})";

    private static string FormatWeights(IEnumerable<BoneWeight> weights)
    {
        return string.Join(";", weights.Select(weight => $"{weight.BoneName}:{weight.BoneIndex}:{weight.Weight:0.###}"));
    }
}

