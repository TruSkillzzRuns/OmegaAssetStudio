using System.Collections.Generic;

namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosMigrationReport
{
    public string FilePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public int NameCount { get; set; }

    public int ImportCount { get; set; }

    public int ExportCount { get; set; }

    public int SkeletalMeshCount { get; set; }

    public int StaticMeshCount { get; set; }

    public int TextureCount { get; set; }

    public int AnimationCount { get; set; }

    public int MaterialCount { get; set; }

    public string DetectedVersionTag { get; set; } = string.Empty;

    public string CompressionMethod { get; set; } = string.Empty;

    public List<ThanosMigrationFinding> Findings { get; } = [];
}

