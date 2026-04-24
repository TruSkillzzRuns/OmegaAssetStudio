using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor;

public sealed partial class WorldEditorPage : Page
{
    public PropViewModel ViewModel { get; } = new();

    public WorldEditorPage()
    {
        InitializeComponent();
    }
}

