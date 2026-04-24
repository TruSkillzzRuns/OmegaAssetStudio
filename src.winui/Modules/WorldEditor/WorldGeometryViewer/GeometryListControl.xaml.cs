using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldGeometryViewer;

public sealed partial class GeometryListControl : UserControl
{
    public WorldGeometryViewerViewModel? ViewModel { get; set; }

    public GeometryListControl()
    {
        InitializeComponent();
    }

    private void LoadGeometry_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.LoadGeometry();
    }
}

