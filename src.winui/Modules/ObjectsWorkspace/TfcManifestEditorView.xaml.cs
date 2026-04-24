using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace;

public sealed partial class TfcManifestEditorView : UserControl
{
    private readonly TfcManifestEditorViewModel viewModel = new();

    public TfcManifestEditorView()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OpenManifestButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".bin");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        if (await picker.PickSingleFileAsync() is { } file)
        {
            viewModel.SetManifestPath(file.Path);
            await viewModel.OpenManifestAsync(file.Path);
            EntriesList.SelectedIndex = viewModel.Entries.Count > 0 ? 0 : -1;
        }
    }

    private async void SaveManifestButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(viewModel.ManifestPath))
        {
            FileSavePicker picker = new();
            picker.FileTypeChoices.Add("Binary Manifest", [".bin"]);
            picker.SuggestedFileName = "TextureFileCacheManifest.bin";

            nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            if (await picker.PickSaveFileAsync() is { } file)
                await viewModel.SaveManifestAsync(file.Path);
            return;
        }

        await viewModel.SaveManifestAsync(viewModel.ManifestPath);
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        viewModel.SelectedEntry = EntriesList.SelectedItem as OmegaAssetStudio.TfcManifest.TfcManifestEntry;
    }

    private void AddEntryButton_Click(object sender, RoutedEventArgs e)
    {
        viewModel.AddEntry();
        EntriesList.SelectedItem = viewModel.SelectedEntry;
    }

    private void RemoveEntryButton_Click(object sender, RoutedEventArgs e)
    {
        viewModel.RemoveSelectedEntry();
        EntriesList.SelectedItem = viewModel.SelectedEntry;
    }

    private void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ApplySelectedEntryChanges();
    }

    private void PackageNameHeader_Click(object sender, RoutedEventArgs e)
    {
        viewModel.SortEntriesByPackageName();
        EntriesList.SelectedItem = viewModel.SelectedEntry;
    }

    private void ValidationHeader_Click(object sender, RoutedEventArgs e)
    {
        viewModel.SortValidationResults();
    }

    private async void InjectEntriesFileButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".bin");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        if (await picker.PickSingleFileAsync() is { } file)
            await viewModel.InjectEntriesAsync(file.Path);
    }

    private async void InjectEntriesFromMigrationButton_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        if (await picker.PickSingleFolderAsync() is { } folder)
            await viewModel.InjectEntriesAsync(folder.Path);
    }
}

