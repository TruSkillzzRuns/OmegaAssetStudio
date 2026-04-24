using System.Collections.ObjectModel;

namespace OmegaAssetStudio.WinUI.Models;

public sealed class ObjectTreeItem
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PathName { get; set; } = string.Empty;
    public int TableIndex { get; set; }
    public bool IsImport { get; set; }
    public bool IsExport { get; set; }
    public ObservableCollection<ObjectTreeItem> Children { get; } = [];

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Kind) ? Name : $"{Name} {Kind}";
    }
}

