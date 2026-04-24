using Microsoft.UI.Xaml.Media;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class MeshPreviewPoint
{
    public int Index { get; set; }

    public string Label { get; set; } = string.Empty;

    public double Left { get; set; }

    public double Top { get; set; }

    public bool IsSelected { get; set; }

    public Brush FillBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue);

    public Brush BorderBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.White);
}

