using System.Collections.Generic;
using System.Linq;

namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosDependencyReport
{
    public string FilePath { get; set; } = string.Empty;

    public string? SourceUpkPath { get; set; }

    public string? Client148Root { get; set; }

    public string? Client152Root { get; set; }

    public string? Summary { get; set; }

    public List<ThanosDependencyItem> MissingDependencies { get; set; } = [];

    public int MissingDependencyCount => MissingDependencies.Count(item => item.MissingInClient);
}

