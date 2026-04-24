using System;

namespace OmegaAssetStudio.ThanosMigration.Models;

public enum ThanosMigrationStepStatus
{
    Pending,
    Done,
    Skipped,
    Failed
}

public sealed class ThanosMigrationStep
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ThanosMigrationStepStatus Status { get; set; } = ThanosMigrationStepStatus.Pending;

    public string? Reason { get; set; }

    public Exception? Exception { get; set; }

    public override string ToString()
        => $"{Name} [{Status}]";
}

