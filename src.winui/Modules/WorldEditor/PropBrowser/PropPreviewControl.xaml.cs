using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.PropBrowser;

public sealed partial class PropPreviewControl : UserControl
{
    public PropViewModel? ViewModel { get; set; }

    public PropPreviewControl()
    {
        InitializeComponent();
    }
}

