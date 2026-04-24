using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.ThanosMigration.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed partial class UpkMigrationView : Page
{
    public UpkMigrationViewModel ViewModel { get; }

    public UpkMigrationView()
    {
        InitializeComponent();
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UpkMigrationService migrationService = new(dispatcherQueue);
        ThanosStructuralMigrationService structuralService = new();
        ThanosTextureMigrationService textureService = new();
        TfcManifestService tfcManifestService = new();
        ViewModel = new UpkMigrationViewModel(migrationService, structuralService, textureService, tfcManifestService, dispatcherQueue);
        ViewModel.SelectUpksRequestedAsync = SelectUpksAsync;
        ViewModel.BrowseOutputDirectoryRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseTextureManifestDirectoryRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.PrototypeMerger.BrowseReportRequestedAsync = BrowseDependencyReportAsync;
        ViewModel.PrototypeMerger.BrowseClient148RootRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.PrototypeMerger.BrowseClient152RootRequestedAsync = BrowseOutputDirectoryAsync;
        DataContext = ViewModel;
    }

    private async Task<IReadOnlyList<string>> SelectUpksAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        List<string> paths = [];
        foreach (StorageFile file in files)
            paths.Add(file.Path);

        return paths;
    }

    private async Task<string?> BrowseOutputDirectoryAsync()
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> BrowseDependencyReportAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".upk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static void InitializePicker(FileOpenPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static void InitializePicker(FolderPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }
}

