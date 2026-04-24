using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldGeometryViewer;

public sealed partial class GeometryPreviewControl : UserControl
{
    public WorldGeometryViewerViewModel? ViewModel { get; set; }

    public GeometryPreviewControl()
    {
        InitializeComponent();
    }
}

