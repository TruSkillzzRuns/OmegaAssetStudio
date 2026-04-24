namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;

public sealed class UpkImportEntry
{
    public int TableIndex { get; set; }

    public string ObjectName { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string OuterName { get; set; } = string.Empty;

    public bool IsImport { get; set; } = true;

    public bool IsExport { get; set; }
}
