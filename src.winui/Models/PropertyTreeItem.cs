using System.Collections.ObjectModel;

namespace OmegaAssetStudio.WinUI.Models;

public sealed class PropertyTreeItem
{
    public string Text { get; set; } = string.Empty;
    public object? Tag { get; set; }
    public ObservableCollection<PropertyTreeItem> Children { get; } = [];

    public override string ToString()
    {
        return Text;
    }
}

