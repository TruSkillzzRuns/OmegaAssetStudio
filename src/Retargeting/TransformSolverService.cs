using System.Numerics;

namespace OmegaAssetStudio.Retargeting;

public sealed record TransformSolveReport(
    int BonesFixed,
    int VerticesFixed,
    int InvalidTransforms,
    IReadOnlyList<string> Notes);

public sealed class TransformSolverService
{
    private const float MinimumScale = 0.0001f;

    public TransformSolveReport Analyze(RetargetMesh mesh, Action<string>? log = null)
    {
        if (mesh is null)
            throw new ArgumentNullException(nameof(mesh));

        int bonesFixed = 0;
        int verticesFixed = 0;
        int invalidTransforms = 0;
        List<string> notes = [];

        foreach (RetargetBone bone in mesh.Bones)
        {
            if (!IsFinite(bone.LocalTransform) || !IsFinite(bone.GlobalTransform))
            {
                invalidTransforms++;
                bonesFixed++;
                notes.Add($"Non-finite transform detected on bone '{bone.Name}'.");
            }
            else if (!Matrix4x4.Decompose(bone.GlobalTransform, out Vector3 scale, out _, out _))
            {
                invalidTransforms++;
                bonesFixed++;
                notes.Add($"Bone '{bone.Name}' could not be decomposed cleanly.");
            }
            else if (MathF.Abs(scale.X) < MinimumScale || MathF.Abs(scale.Y) < MinimumScale || MathF.Abs(scale.Z) < MinimumScale)
            {
                bonesFixed++;
                notes.Add($"Bone '{bone.Name}' has near-zero scale.");
            }
        }

        foreach (RetargetSection section in mesh.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) || !IsFinite(vertex.Tangent) || !IsFinite(vertex.Bitangent))
                {
                    verticesFixed++;
                    notes.Add($"Vertex in section '{section.Name}' contains non-finite position or basis vectors.");
                }
            }
        }

        string summary = $"Transform analysis complete. Bones needing cleanup: {bonesFixed}, vertices needing cleanup: {verticesFixed}, invalid transforms: {invalidTransforms}.";
        notes.Insert(0, summary);
        log?.Invoke(summary);
        return new TransformSolveReport(bonesFixed, verticesFixed, invalidTransforms, notes);
    }

    public RetargetMesh Apply(RetargetMesh mesh, TransformSolveReport report, Action<string>? log = null)
    {
        if (mesh is null)
            throw new ArgumentNullException(nameof(mesh));

        RetargetMesh solved = mesh.DeepClone();
        Matrix4x4[] globals = new Matrix4x4[solved.Bones.Count];

        for (int i = 0; i < solved.Bones.Count; i++)
        {
            RetargetBone bone = solved.Bones[i];
            Matrix4x4 parentGlobal = bone.ParentIndex >= 0 && bone.ParentIndex < globals.Length
                ? globals[bone.ParentIndex]
                : Matrix4x4.Identity;

            Matrix4x4 local = SanitizeTransform(bone.LocalTransform, parentGlobal);
            Matrix4x4 global = bone.ParentIndex >= 0 && bone.ParentIndex < globals.Length
                ? local * parentGlobal
                : local;

            bone.LocalTransform = local;
            bone.GlobalTransform = global;
            globals[i] = global;
        }

        foreach (RetargetSection section in solved.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                vertex.Position = SanitizeVector(vertex.Position);
                vertex.Normal = NormalizeOrUnit(SanitizeVector(vertex.Normal), Vector3.UnitY);
                vertex.Tangent = NormalizeOrUnit(SanitizeVector(vertex.Tangent), Vector3.UnitX);
                vertex.Bitangent = NormalizeOrUnit(SanitizeVector(vertex.Bitangent), Vector3.UnitZ);
            }
        }

        solved.RebuildBoneLookup();
        log?.Invoke($"Transform solver applied. Bones fixed: {report.BonesFixed}, vertices fixed: {report.VerticesFixed}, invalid transforms: {report.InvalidTransforms}.");
        return solved;
    }

    private static Matrix4x4 SanitizeTransform(Matrix4x4 value, Matrix4x4 fallbackParent)
    {
        if (!IsFinite(value))
            return fallbackParent;

        if (!Matrix4x4.Decompose(value, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            return fallbackParent;

        scale = SanitizeScale(scale);
        rotation = NormalizeOrIdentity(rotation);
        translation = SanitizeVector(translation);
        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(rotation) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static Vector3 SanitizeScale(Vector3 scale)
    {
        return new Vector3(
            MathF.Abs(scale.X) < MinimumScale ? 1.0f : scale.X,
            MathF.Abs(scale.Y) < MinimumScale ? 1.0f : scale.Y,
            MathF.Abs(scale.Z) < MinimumScale ? 1.0f : scale.Z);
    }

    private static Quaternion NormalizeOrIdentity(Quaternion value)
    {
        return IsFinite(value) && value.LengthSquared() > 0.000001f
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;
    }

    private static Vector3 SanitizeVector(Vector3 value)
    {
        return IsFinite(value) ? value : Vector3.Zero;
    }

    private static Vector3 NormalizeOrUnit(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;
    }

    private static bool IsFinite(Matrix4x4 value)
    {
        return IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14) &&
               IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24) &&
               IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34) &&
               IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

