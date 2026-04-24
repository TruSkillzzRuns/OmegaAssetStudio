using System;

namespace OmegaAssetStudio.ThanosMigration.Models;

public enum ThanosMigrationSeverity
{
    Info,
    Warning,
    Critical
}

public enum ThanosMigrationCategory
{
    Header,
    Names,
    Imports,
    Exports,
    Compression,
    Unknown,
    Textures,
    Tfc,
    World,
    Kismet,
    Script,
    Streaming
}

public sealed class ThanosMigrationFinding
{
    public ThanosMigrationSeverity Severity { get; set; }

    public ThanosMigrationCategory Category { get; set; }

    public string Message { get; set; } = string.Empty;

    public long? Offset { get; set; }

    public string? RecommendedAction { get; set; }

    public override string ToString()
        => $"{Severity} {Category}: {Message}";
}

