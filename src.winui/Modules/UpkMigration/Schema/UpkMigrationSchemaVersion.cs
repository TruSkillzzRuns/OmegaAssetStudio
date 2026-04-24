using System;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed record UpkMigrationSchemaVersion
{
    public UpkMigrationSchemaVersion(string value, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Schema version is required.", nameof(value));

        Value = value;
        Label = string.IsNullOrWhiteSpace(label) ? value : label;
    }

    public string Value { get; }
    public string Label { get; }
    public override string ToString() => Value;
}

