using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmegaAssetStudio.ThanosMigration.Models;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosPrototypeMergePlanner
{
    public IReadOnlyList<ThanosPrototypeMergePlan> BuildMergePlans(IReadOnlyList<ThanosPrototypeSource> sources, string client152Root)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(client152Root);

        string fullRoot = Path.GetFullPath(client152Root);
        Directory.CreateDirectory(fullRoot);

        string raidTargetPath = Path.Combine(fullRoot, "MHGameContent_Raid_Thanos_152.upk");
        string genericTargetPath = Path.Combine(fullRoot, "Thanos_MergedContent_152.upk");

        List<ThanosPrototypeMergePlan> plans = [];

        foreach (var group in sources.GroupBy(source => ResolveTargetPath(source, fullRoot, raidTargetPath, genericTargetPath), StringComparer.OrdinalIgnoreCase))
        {
            plans.Add(new ThanosPrototypeMergePlan
            {
                TargetUpkPath = group.Key,
                SourcePrototypes = group.ToArray(),
                Notes = $"Sources={group.Count():N0}"
            });
        }

        return plans
            .OrderBy(plan => plan.TargetUpkPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveTargetPath(ThanosPrototypeSource source, string client152Root, string raidTargetPath, string genericTargetPath)
    {
        string packageName = string.IsNullOrWhiteSpace(source.Dependency.PackageName)
            ? Path.GetFileNameWithoutExtension(source.SourceUpkPath)
            : source.Dependency.PackageName;

        string? matched = Directory.EnumerateFiles(client152Root, "*.upk", SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), packageName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(Path.GetFileName(path), packageName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(matched))
            return matched;

        if (packageName.Contains("raid", StringComparison.OrdinalIgnoreCase) ||
            packageName.Contains("thanos", StringComparison.OrdinalIgnoreCase) ||
            source.ExportPathLikeRaidHint())
        {
            return raidTargetPath;
        }

        return genericTargetPath;
    }
}

internal static class ThanosPrototypeMergePlannerExtensions
{
    public static bool ExportPathLikeRaidHint(this ThanosPrototypeSource source)
    {
        string haystack = string.Join(" ", new[]
        {
            source.Dependency.Name,
            source.Dependency.ClassName,
            source.Dependency.OuterName,
            source.ExportObjectName,
            source.ExportClassName,
            source.ExportOuterName
        }.Where(item => !string.IsNullOrWhiteSpace(item)));

        return haystack.Contains("raid", StringComparison.OrdinalIgnoreCase) ||
               haystack.Contains("thanos", StringComparison.OrdinalIgnoreCase);
    }
}

