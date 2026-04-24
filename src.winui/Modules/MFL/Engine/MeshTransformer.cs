using System.Linq;
using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class MeshTransformer
{
    public Mesh AlignToSharedReferencePose(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Mesh aligned = mesh.Clone();
        MeshFrame frame = GetReferenceFrame(aligned);
        TransformMesh(aligned, frame.Pivot, Quaternion.Inverse(frame.Rotation), 1.0f, Vector3.Zero);
        return aligned;
    }

    public Mesh TransformToTargetSpace(Mesh source, Mesh target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Mesh transformed = source.Clone();
        if (source.Vertices.Count == 0 || target.Vertices.Count == 0)
            return transformed;

        MeshFrame sourceFrame = GetReferenceFrame(source);
        MeshFrame targetFrame = GetReferenceFrame(target);
        float scale = targetFrame.Scale / sourceFrame.Scale;
        Quaternion rotation = Quaternion.Normalize(targetFrame.Rotation * Quaternion.Inverse(sourceFrame.Rotation));
        TransformMesh(transformed, sourceFrame.Pivot, rotation, scale, targetFrame.Pivot);
        return transformed;
    }

    private static MeshFrame GetReferenceFrame(Mesh mesh)
    {
        if (mesh.Bones.Count > 0)
        {
            Bone? root = mesh.Bones.FirstOrDefault();
            if (root is not null)
                return new MeshFrame(root.BindPosition, root.BindRotation, MathF.Max(0.0001f, mesh.Bounds.Size.Length()));
        }

        return new MeshFrame(mesh.Bounds.Center, Quaternion.Identity, MathF.Max(0.0001f, mesh.Bounds.Size.Length()));
    }

    private static void TransformMesh(Mesh mesh, Vector3 sourcePivot, Quaternion rotation, float scale, Vector3 targetPivot)
    {
        Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);

        for (int index = 0; index < mesh.Vertices.Count; index++)
        {
            Vertex vertex = mesh.Vertices[index];
            Vector3 localPosition = Vector3.Transform(vertex.Position - sourcePivot, rotationMatrix);
            vertex.Position = targetPivot + (localPosition * scale);
            vertex.Normal = NormalizeOrFallback(Vector3.TransformNormal(vertex.Normal, rotationMatrix), Vector3.UnitY);
            vertex.Tangent = NormalizeOrFallback(Vector3.TransformNormal(vertex.Tangent, rotationMatrix), Vector3.UnitX);
            vertex.Bitangent = NormalizeOrFallback(Vector3.TransformNormal(vertex.Bitangent, rotationMatrix), Vector3.UnitZ);
        }

        foreach (Bone bone in mesh.Bones)
        {
            Vector3 localPosition = Vector3.Transform(bone.BindPosition - sourcePivot, rotationMatrix);
            bone.BindPosition = targetPivot + (localPosition * scale);
            bone.BindRotation = Quaternion.Normalize(rotation * bone.BindRotation);
        }

        foreach (Socket socket in mesh.Sockets)
        {
            Vector3 localPosition = Vector3.Transform(socket.Position - sourcePivot, rotationMatrix);
            socket.Position = targetPivot + (localPosition * scale);
            socket.Rotation = Quaternion.Normalize(rotation * socket.Rotation);
        }

        mesh.RecalculateBounds();
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        if (value == Vector3.Zero)
            return fallback;

        Vector3 normalized = Vector3.Normalize(value);
        return float.IsNaN(normalized.X) || float.IsNaN(normalized.Y) || float.IsNaN(normalized.Z) ? fallback : normalized;
    }

    private readonly record struct MeshFrame(Vector3 Pivot, Quaternion Rotation, float Scale);
}

