using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.ViewModels;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Views;

public sealed partial class ObjectsWorkspaceView : UserControl
{
    private readonly ObjectsWorkspaceViewModel viewModel = new();

    public ObjectsWorkspaceView()
    {
        InitializeComponent();
        DataContext = viewModel;
        ExportsList.ItemsSource = viewModel.Exports;
    }

    public void LoadRawBytes(byte[] bytes, string? title = null)
    {
        HexEditorPanel.LoadBytes(bytes, title);
        viewModel.StatusText = $"Loaded {bytes.Length:N0} byte{(bytes.Length == 1 ? string.Empty : "s")} from selection.";
    }

    private async void OpenUpkButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        if (await picker.PickSingleFileAsync() is not { } file)
            return;

        await viewModel.LoadUpkAsync(file.Path);
        if (viewModel.Exports.Count > 0)
        {
            ExportsList.SelectedIndex = 0;
            LoadSelectedExportToEditor(viewModel.Exports[0]);
        }
    }

    private async void SaveUpkButton_Click(object sender, RoutedEventArgs e)
    {
        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("UPK Package", [".upk"]);
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(viewModel.PackagePath)
            ? "rebuilt.upk"
            : Path.GetFileNameWithoutExtension(viewModel.PackagePath) + "_rebuild";

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        if (await picker.PickSaveFileAsync() is not { } file)
            return;

        if (viewModel.CurrentPackage is null)
        {
            viewModel.StatusText = "Open a UPK before saving.";
            return;
        }

        if (HexEditorPanel.TryGetEditedBytes(out byte[] bytes, out string message) && viewModel.SelectedExport is not null)
        {
            viewModel.SelectedExport.RawData = bytes;
            viewModel.SelectedExport.SerialSize = bytes.Length;
            viewModel.StatusText = message;
        }

        await viewModel.SaveRebuiltAsync(file.Path);
    }

    private void ExportsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExportsList.SelectedItem is not UpkExportEntry export)
            return;

        viewModel.LoadExport(export);
        LoadSelectedExportToEditor(export);
    }

    private void ApplyToExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedExport is null)
        {
            viewModel.StatusText = "Select an export first.";
            return;
        }

        if (!HexEditorPanel.TryGetEditedBytes(out byte[] bytes, out string message))
        {
            viewModel.StatusText = message;
            return;
        }

        viewModel.SelectedExport.RawData = bytes;
        viewModel.SelectedExport.SerialSize = bytes.Length;
        viewModel.StatusText = $"Applied {bytes.Length:N0} byte{(bytes.Length == 1 ? string.Empty : "s")} to {viewModel.SelectedExport.ObjectName}.";
    }

    private void LoadSelectedExportToEditor(UpkExportEntry export)
    {
        HexEditorPanel.LoadBytes(export.RawData, export.ObjectName);
    }
}
