using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.PropBrowser;

public sealed partial class PropListControl : UserControl
{
    public PropViewModel? ViewModel { get; set; }

    public PropListControl()
    {
        InitializeComponent();
    }

    private void LoadProps_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.LoadProps();
    }
}

