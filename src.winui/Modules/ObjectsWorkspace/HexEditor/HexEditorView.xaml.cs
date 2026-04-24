using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.ViewModels;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.HexEditor;

public sealed partial class HexEditorView : UserControl
{
    private readonly HexEditorViewModel viewModel = new();
    private bool suppressTextChange;

    public HexEditorView()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void LoadBytes(byte[] bytes, string? title = null)
    {
        suppressTextChange = true;
        viewModel.LoadBytes(bytes, title);
        HexTextBox.Text = viewModel.HexText;
        suppressTextChange = false;
    }

    public bool TryGetEditedBytes(out byte[] bytes, out string message)
    {
        return viewModel.TryCommitHexText(HexTextBox.Text, out bytes, out message);
    }

    public void Revert()
    {
        suppressTextChange = true;
        viewModel.Revert();
        HexTextBox.Text = viewModel.HexText;
        suppressTextChange = false;
    }

    private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressTextChange)
            return;

        viewModel.SetHexText(HexTextBox.Text);
    }

    private void NormalizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.TryCommitHexText(HexTextBox.Text, out _, out string message))
        {
            viewModel.StatusText = message;
            return;
        }

        suppressTextChange = true;
        HexTextBox.Text = viewModel.HexText;
        suppressTextChange = false;
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        Revert();
    }

    private async void CopyHexButton_Click(object sender, RoutedEventArgs e)
    {
        DataPackage data = new();
        data.SetText(HexTextBox.Text);
        Clipboard.SetContent(data);
        await Task.CompletedTask;
    }
}
