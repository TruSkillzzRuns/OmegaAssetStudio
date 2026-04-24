using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldGeometryViewer;

public sealed partial class WorldGeometryViewerView : Page
{
    public WorldGeometryViewerViewModel ViewModel { get; } = new();

    public WorldGeometryViewerView()
    {
        InitializeComponent();
    }

    private void LoadGeometry_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.LoadGeometry();
    }
}

