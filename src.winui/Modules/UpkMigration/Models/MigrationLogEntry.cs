namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class MigrationLogEntry
{
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }

    public override string ToString() => $"[{Timestamp}] {Level}: {Message}";
}

