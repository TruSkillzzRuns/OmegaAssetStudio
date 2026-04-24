namespace OmegaAssetStudio.Retargeting;

public sealed class RetargetMappingService
{
    private readonly BoneMapper _boneMapper = new();
    private readonly MappingProfileManager _profileManager = new();

    public BoneMappingResult AutoMap(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string>? manualOverrides = null,
        Action<string>? log = null)
    {
        BoneMappingResult result = _boneMapper.AutoMap(sourceMesh, targetSkeleton, log);
        ApplyOverrides(sourceMesh, result, manualOverrides, log);
        return result;
    }

    public BoneMappingResult ApplyProfile(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        RetargetMappingProfile profile,
        Action<string>? log = null)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        BoneMappingResult result = new();
        foreach (var mapping in profile.Mappings)
            result.Mapping[mapping.Key] = mapping.Value;

        ApplyOverrides(sourceMesh, result, profile.ManualOverrides, log);
        if (!string.IsNullOrWhiteSpace(profile.TargetSkeletonPath) &&
            !string.Equals(profile.TargetSkeletonPath, targetSkeleton.SourcePath, StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke($"Loaded profile '{profile.ProfileName}' was authored for a different target skeleton.");
        }

        log?.Invoke($"Loaded mapping profile '{profile.ProfileName}' with {result.Mapping.Count} mapped bone(s).");
        return result;
    }

    public void SaveProfile(
        string profileName,
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        BoneMappingResult mapping,
        IReadOnlyDictionary<string, string>? manualOverrides = null,
        Action<string>? log = null)
    {
        if (sourceMesh is null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (targetSkeleton is null)
            throw new ArgumentNullException(nameof(targetSkeleton));
        if (mapping is null)
            throw new ArgumentNullException(nameof(mapping));

        RetargetMappingProfile profile = new()
        {
            ProfileName = profileName,
            SourceSkeletonPath = sourceMesh.SourcePath,
            TargetSkeletonPath = targetSkeleton.SourcePath,
            CreatedUtc = DateTime.UtcNow,
            Mappings = new Dictionary<string, string>(mapping.Mapping, StringComparer.OrdinalIgnoreCase),
            ManualOverrides = manualOverrides is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(manualOverrides, StringComparer.OrdinalIgnoreCase)
        };

        _profileManager.SaveProfile(profile);
        log?.Invoke($"Saved mapping profile '{profileName}'.");
    }

    public bool TryLoadProfile(string profileName, out RetargetMappingProfile? profile, out string path, out string error)
    {
        return _profileManager.TryLoadProfile(profileName, out profile, out path, out error);
    }

    public IReadOnlyList<string> ListProfiles()
    {
        return _profileManager.ListProfiles();
    }

    public string GetProfilePath(string profileName)
    {
        return _profileManager.GetProfilePath(profileName);
    }

    public void ApplyManualOverride(
        RetargetMesh sourceMesh,
        BoneMappingResult mapping,
        string sourceBoneName,
        string targetBoneName,
        Action<string>? log = null)
    {
        if (sourceMesh is null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (mapping is null)
            throw new ArgumentNullException(nameof(mapping));

        if (string.IsNullOrWhiteSpace(sourceBoneName))
            throw new InvalidOperationException("A source bone name is required for manual override.");
        if (string.IsNullOrWhiteSpace(targetBoneName))
            throw new InvalidOperationException("A target bone name is required for manual override.");

        mapping.Mapping[sourceBoneName] = targetBoneName;
        RebuildUnmapped(sourceMesh, mapping);
        log?.Invoke($"Applied manual override {sourceBoneName} -> {targetBoneName}.");
    }

    private static void ApplyOverrides(
        RetargetMesh sourceMesh,
        BoneMappingResult result,
        IReadOnlyDictionary<string, string>? overrides,
        Action<string>? log)
    {
        if (overrides is not null)
        {
            foreach ((string sourceBone, string targetBone) in overrides)
            {
                if (string.IsNullOrWhiteSpace(sourceBone) || string.IsNullOrWhiteSpace(targetBone))
                    continue;

                result.Mapping[sourceBone] = targetBone;
                log?.Invoke($"Applied mapping override {sourceBone} -> {targetBone}.");
            }
        }

        RebuildUnmapped(sourceMesh, result);
    }

    private static void RebuildUnmapped(RetargetMesh sourceMesh, BoneMappingResult result)
    {
        result.UnmappedBones.Clear();
        foreach (RetargetBone sourceBone in sourceMesh.Bones)
        {
            if (!result.Mapping.TryGetValue(sourceBone.Name, out string? mappedBone) || string.IsNullOrWhiteSpace(mappedBone))
                result.UnmappedBones.Add(sourceBone.Name);
        }
    }
}

