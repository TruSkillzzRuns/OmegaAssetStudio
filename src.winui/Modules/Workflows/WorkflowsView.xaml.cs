using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace OmegaAssetStudio.WinUI.Modules.Workflows;

public sealed partial class WorkflowsView : Window
{
    public WorkflowsViewModel ViewModel { get; } = new();

    public WorkflowsView()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1100, 760));
        RootGrid.DataContext = ViewModel;
    }
}

