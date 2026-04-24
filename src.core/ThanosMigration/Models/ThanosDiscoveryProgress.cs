namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosDiscoveryProgress
{
    public string CurrentItem { get; set; } = string.Empty;

    public string CurrentFile { get; set; } = string.Empty;

    public int ProcessedItems { get; set; }

    public int TotalItems { get; set; }

    public string Status { get; set; } = string.Empty;
}

