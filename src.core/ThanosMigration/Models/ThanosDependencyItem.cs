namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosDependencyItem
{
    public string Name { get; set; } = string.Empty;

    public string ObjectPath { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string OuterName { get; set; } = string.Empty;

    public string ReferenceKind { get; set; } = string.Empty;

    public string? SourceUpkPath { get; set; }

    public int ExportIndex { get; set; }

    public bool MissingInClient { get; set; } = true;

    public string? Details { get; set; }

    public override string ToString() => Name;
}

