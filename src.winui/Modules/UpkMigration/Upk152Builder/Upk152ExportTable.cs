namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk152Builder;

public sealed class Upk152ExportTable
{
    public List<Upk152ExportEntry> Entries { get; } = [];
}

public sealed class Upk152ExportEntry
{
    public string PathName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public int SerialSize { get; set; }
}

