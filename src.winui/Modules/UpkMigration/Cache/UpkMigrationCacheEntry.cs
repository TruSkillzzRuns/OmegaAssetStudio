using System;
using System.Collections.Generic;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkMigrationCacheEntry
{
    public string SourcePath { get; set; } = string.Empty;
    public long SourceLength { get; set; }
    public DateTime SourceLastWriteUtc { get; set; }
    public string SchemaVersion { get; set; } = string.Empty;
    public string AnalyzerVersion { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public string CacheKey { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int MigratedMeshes { get; set; }
    public int MigratedTextures { get; set; }
    public int MigratedAnimations { get; set; }
    public int MigratedMaterials { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

