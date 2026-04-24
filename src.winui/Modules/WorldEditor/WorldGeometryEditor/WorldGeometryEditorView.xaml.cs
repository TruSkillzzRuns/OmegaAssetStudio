using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldGeometryEditor;

public sealed partial class WorldGeometryEditorView : Page
{
    public WorldGeometryEditorViewModel ViewModel { get; } = new();

    public WorldGeometryEditorView()
    {
        InitializeComponent();
    }

    private void LoadSelection_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadSelectedGeometry();
    }

    private void ApplyTransform_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyTransform();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DuplicateSelected();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteSelected();
    }
}

