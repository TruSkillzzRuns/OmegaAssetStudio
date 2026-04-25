using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor;

public sealed partial class WorldEditorPage : Page
{
    public WorldPreviewViewModel PreviewViewModel { get; } = new();
    public GeometryViewModel GeometryViewModel { get; } = new();
    public PropViewModel ViewModel { get; } = new();
    public CollisionViewModel CollisionViewModel { get; } = new();
    public NavmeshViewModel NavmeshViewModel { get; } = new();
    public LightingViewModel LightingViewModel { get; } = new();
    public TriggerViewModel TriggerViewModel { get; } = new();
    public MinimapViewModel MinimapViewModel { get; } = new();

    public WorldEditorPage()
    {
        InitializeComponent();
    }

    private void LoadWorldData_Click(object sender, RoutedEventArgs e)
    {
        string upkPath = SourcePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(upkPath))
            return;

        PreviewViewModel.LoadZone(upkPath);

        GeometryViewModel.SourceUpkPath = upkPath;
        GeometryViewModel.LoadGeometry();

        ViewModel.SourceUpkPath = upkPath;
        ViewModel.LoadProps();

        CollisionViewModel.SourceUpkPath = upkPath;
        CollisionViewModel.LoadCollisionVolumes();

        NavmeshViewModel.SourceUpkPath = upkPath;
        NavmeshViewModel.LoadNavmesh();

        LightingViewModel.SourceUpkPath = upkPath;
        LightingViewModel.LoadLighting();

        TriggerViewModel.SourceUpkPath = upkPath;
        TriggerViewModel.LoadTriggers();

        MinimapViewModel.SourceUpkPath = upkPath;
        MinimapViewModel.LoadMinimap();
    }
}
