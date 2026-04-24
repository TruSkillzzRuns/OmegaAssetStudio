using OmegaAssetStudio.WinUI.Models;
using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.WinUI.Modules.Workflows;
using OmegaAssetStudio.WinUI.Modules.MFL.Rendering;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Navigation;

namespace OmegaAssetStudio.WinUI.Modules.MFL;

public sealed partial class MFLView : Page
{
    public MFLViewModel ViewModel { get; } = new();

    public MFLView()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
        DataContext = ViewModel;
        MeshViewport.Scene = ViewModel.Scene;
        MeshViewport.Camera = ViewModel.Camera;
        MeshViewport.RenderDiagnosticsChanged += MeshViewport_RenderDiagnosticsChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is WorkspaceLaunchContext context)
            await ViewModel.HandleWorkspaceLaunchAsync(context).ConfigureAwait(true);
    }

    private void WorkflowsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        WorkflowsService.OpenWorkflowsWindow();
    }

    private void D3D11SwapChainHost_MeshHitSelected(object sender, MeshHitResult hit)
    {
        ViewModel.HandleViewportHit(hit);
    }

    private void MeshViewport_RenderDiagnosticsChanged(object? sender, string diagnostics)
    {
        OmegaAssetStudio.WinUI.App.WriteDiagnosticsLog("MFL.Viewport", diagnostics);
        if (DispatcherQueue.HasThreadAccess)
        {
            ViewModel.ViewportDiagnosticsText = diagnostics;
            return;
        }

        DispatcherQueue.TryEnqueue(() => ViewModel.ViewportDiagnosticsText = diagnostics);
    }

    private async void ImportMeshAButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        await ViewModel.ImportMeshAAsync(file.Path);
    }

    private async void ImportMeshBButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        await ViewModel.ImportMeshBAsync(file.Path);
    }

}

