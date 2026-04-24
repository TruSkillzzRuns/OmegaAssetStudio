using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.Settings;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsView()
    {
        InitializeComponent();
    }
}
