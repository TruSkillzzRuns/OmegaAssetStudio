using System;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class MigrationEnvelope<T>
{
    public string SchemaVersion { get; init; } = string.Empty;
    public UpkMigrationSchemaVersion Version { get; init; } = new(UpkMigrationSchemaManager.CurrentVersion.Value);
    public string Inspector { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
    public T Entity { get; init; } = default!;
}

