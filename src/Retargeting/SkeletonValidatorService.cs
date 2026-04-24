using System.Numerics;

namespace OmegaAssetStudio.Retargeting;

public sealed class SkeletonValidatorService
{
    public ValidationResult Validate(
        RetargetMesh? sourceMesh,
        SkeletonDefinition? targetSkeleton,
        IReadOnlyDictionary<string, string>? boneMapping = null,
        Action<string>? log = null)
    {
        ValidationResult result = new();

        if (sourceMesh is null)
        {
            result.Issues.Add(new ValidationIssue(ValidationSeverity.Error, "SourceMesh", "No source mesh is loaded."));
            return result;
        }

        if (targetSkeleton is null)
        {
            result.Issues.Add(new ValidationIssue(ValidationSeverity.Error, "TargetSkeleton", "No target skeleton is loaded."));
            return result;
        }

        sourceMesh.RebuildBoneLookup();
        targetSkeleton.RebuildBoneLookup();

        result.SourceBoneCount = sourceMesh.Bones.Count;
        result.TargetBoneCount = targetSkeleton.Bones.Count;

        result.Issues.Add(new ValidationIssue(
            ValidationSeverity.Info,
            "Counts",
            $"Source bones: {result.SourceBoneCount}, target bones: {result.TargetBoneCount}."));

        if (sourceMesh.Bones.Count != targetSkeleton.Bones.Count)
        {
            result.Issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "BoneCount",
                $"Bone counts differ by {Math.Abs(sourceMesh.Bones.Count - targetSkeleton.Bones.Count)}."));
        }

        Dictionary<string, string> mapping = boneMapping is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(boneMapping, StringComparer.OrdinalIgnoreCase);

        HashSet<string> mappedTargets = new(StringComparer.OrdinalIgnoreCase);
        foreach (RetargetBone sourceBone in sourceMesh.Bones)
        {
            if (BoneNameHeuristics.IsTwistBone(sourceBone.Name))
            {
                result.Issues.Add(new ValidationIssue(
                    ValidationSeverity.Info,
                    "TwistBone",
                    "Twist bone detected in source skeleton.",
                    sourceBone.Name));
            }

            if (!mapping.TryGetValue(sourceBone.Name, out string? mappedBone) || string.IsNullOrWhiteSpace(mappedBone))
            {
                result.Issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "MissingMapping",
                    "Source bone does not have a mapped target bone.",
                    sourceBone.Name));
                continue;
            }

            mappedTargets.Add(mappedBone);
            if (!targetSkeleton.BonesByName.TryGetValue(mappedBone, out RetargetBone? targetBone))
            {
                result.Issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "MissingTargetBone",
                    $"Mapped target bone '{mappedBone}' was not found in the target skeleton.",
                    sourceBone.Name));
                continue;
            }

            if (BoneNameHeuristics.IsTwistBone(targetBone.Name))
            {
                result.Issues.Add(new ValidationIssue(
                    ValidationSeverity.Info,
                    "TwistBone",
                    "Mapped target bone is a twist bone.",
                    targetBone.Name));
            }

            ValidateHierarchy(sourceMesh, targetSkeleton, mapping, sourceBone, targetBone, result);
            ValidateScale(sourceMesh, targetSkeleton, mapping, sourceBone, targetBone, result);
        }

        foreach (RetargetBone targetBone in targetSkeleton.Bones)
        {
            if (mappedTargets.Contains(targetBone.Name))
                continue;

            if (BoneNameHeuristics.IsHelperBone(targetBone.Name))
                continue;

            result.Issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "ExtraTargetBone",
                "Target skeleton bone is not referenced by the current mapping.",
                targetBone.Name));
        }

        if (result.ErrorCount == 0)
        {
            result.Issues.Add(new ValidationIssue(
                ValidationSeverity.Info,
                "ValidationComplete",
                result.WarningCount == 0
                    ? "Skeleton validation passed with no warnings."
                    : $"Skeleton validation completed with {result.WarningCount} warning(s)."));
        }

        log?.Invoke($"Validated skeletons: {result.SourceBoneCount} source bone(s), {result.TargetBoneCount} target bone(s), {result.ErrorCount} error(s), {result.WarningCount} warning(s).");
        return result;
    }

    private static void ValidateHierarchy(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string> mapping,
        RetargetBone sourceBone,
        RetargetBone targetBone,
        ValidationResult result)
    {
        if (sourceBone.ParentIndex < 0)
            return;

        RetargetBone sourceParent = sourceMesh.Bones[sourceBone.ParentIndex];
        if (!mapping.TryGetValue(sourceParent.Name, out string? mappedParent) || string.IsNullOrWhiteSpace(mappedParent))
            return;

        if (!targetSkeleton.BonesByName.TryGetValue(mappedParent, out RetargetBone? targetParent))
            return;

        if (targetParent.Name.Equals(targetBone.Name, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "Hierarchy",
                "Mapped bone hierarchy collapsed to the same target bone.",
                sourceBone.Name));
            return;
        }

        if (targetBone.ParentIndex >= 0)
        {
            string targetParentName = targetSkeleton.Bones[targetBone.ParentIndex].Name;
            if (!string.Equals(targetParentName, targetParent.Name, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "Hierarchy",
                    $"Mapped hierarchy differs from the target parent chain ({targetParent.Name} -> {targetParentName}).",
                    sourceBone.Name));
            }
        }
    }

    private static void ValidateScale(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string> mapping,
        RetargetBone sourceBone,
        RetargetBone targetBone,
        ValidationResult result)
    {
        if (sourceBone.ParentIndex < 0)
            return;

        RetargetBone sourceParent = sourceMesh.Bones[sourceBone.ParentIndex];
        if (!mapping.TryGetValue(sourceParent.Name, out string? mappedParent) || string.IsNullOrWhiteSpace(mappedParent))
            return;

        if (!targetSkeleton.BonesByName.TryGetValue(mappedParent, out RetargetBone? targetParent))
            return;

        float sourceLength = Vector3.Distance(sourceParent.GlobalTransform.Translation, sourceBone.GlobalTransform.Translation);
        float targetLength = Vector3.Distance(targetParent.GlobalTransform.Translation, targetBone.GlobalTransform.Translation);
        if (sourceLength <= 1e-4f || targetLength <= 1e-4f)
            return;

        float ratio = targetLength / sourceLength;
        if (!float.IsFinite(ratio) || ratio <= 0.0f)
            return;

        if (ratio < 0.5f || ratio > 2.0f)
        {
            result.Issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "ScaleMismatch",
                $"Bone length ratio is {ratio:0.###}x.",
                sourceBone.Name));
        }
    }
}

