using System.Collections.Generic;

namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosPrototypeMergePlan
{
    public string TargetUpkPath { get; set; } = string.Empty;

    public IReadOnlyList<ThanosPrototypeSource> SourcePrototypes { get; set; } = [];

    public string? Notes { get; set; }
}

