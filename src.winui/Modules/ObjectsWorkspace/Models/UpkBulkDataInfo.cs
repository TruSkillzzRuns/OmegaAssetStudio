namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;

public sealed class UpkBulkDataInfo
{
    public int BulkDataSize { get; set; }

    public long BulkDataOffsetInFile { get; set; }

    public string StorageType { get; set; } = string.Empty;
}
