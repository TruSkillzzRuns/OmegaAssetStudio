using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class RecentUpksPage : Page
{
    public ObservableCollection<RecentUpkEntry> RecentItems { get; } = [];

    public RecentUpksPage()
    {
        InitializeComponent();
        RefreshRecentItems();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshRecentItems();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRecentItems();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        RecentUpkSession.Clear();
        RefreshRecentItems();
    }

    private void OpenInObjectsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "objects");
    }

    private void OpenInMeshButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "mesh");
    }

    private void OpenInTexturesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "textures");
    }

    private void SendToRetargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "retarget");
    }

    private void SendToMeshAButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "mfl", "MeshA");
    }

    private void SendToMeshBButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "mfl", "MeshB");
    }

    private void OpenInOmegaIntelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RecentUpkEntry entry)
            return;

        OpenEntry(entry, "omegaintel");
    }

    private void OpenEntry(RecentUpkEntry entry, string workspaceTag, string targetMeshSlot = "")
    {
        if (App.MainWindow is null || string.IsNullOrWhiteSpace(entry.UpkPath))
            return;

        try
        {
            WorkspaceLaunchContext context = new()
            {
                WorkspaceTag = workspaceTag,
                UpkPath = entry.UpkPath,
                ExportPath = entry.ExportPath,
                TargetMeshSlot = targetMeshSlot,
                ObjectType = string.Empty,
                Title = entry.DisplayTitle,
                Summary = entry.Summary
            };

            RecentUpkSession.RecordWorkspaceLaunch(context);
            App.WriteDiagnosticsLog("RecentUpks", $"Opening {workspaceTag} from recent UPK: {entry.UpkPath}");
            App.MainWindow.NavigateToTag(workspaceTag, context);
        }
        catch (Exception ex)
        {
            App.WriteDiagnosticsLog("RecentUpks", $"Failed to open {workspaceTag} from recent UPK: {ex}");
        }
    }

    private void RefreshRecentItems()
    {
        RecentItems.Clear();
        IReadOnlyList<RecentUpkEntry> entries = RecentUpkSession.GetRecentEntries();
        foreach (RecentUpkEntry entry in entries)
            RecentItems.Add(entry);

        StatusText.Text = RecentItems.Count == 0
            ? "No recent UPKs loaded yet."
            : $"Showing {RecentItems.Count} recent UPK{(RecentItems.Count == 1 ? string.Empty : "s")}.";
    }
}

