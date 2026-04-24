using System.Collections.ObjectModel;
using OmegaAssetStudio.BackupManager;
using OmegaAssetStudio.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class BackupPage : Page
{
    private readonly BackupManagerService _service = new();
    private bool _busy;
    private string? _currentFolder;
    private WorkspaceLaunchContext? _currentContext;

    public ObservableCollection<BackupEntryViewModel> BackupEntries { get; } = [];

    public BackupPage()
    {
        InitializeComponent();
        RootGrid.Background = new ImageBrush
        {
            ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/backup-background.png")),
            Stretch = Stretch.UniformToFill
        };
        BackupListView.ItemsSource = BackupEntries;
        SetEmptyState();
        _ = RestoreLastBackupFolderAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _currentContext = e.Parameter as WorkspaceLaunchContext;
        if (_currentContext is not null && !string.IsNullOrWhiteSpace(_currentContext.UpkPath))
            UseFolder(Path.GetDirectoryName(_currentContext.UpkPath), remember: true);
    }

    private async void UseCurrentUpkFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string? folder = null;
        if (_currentContext is not null && !string.IsNullOrWhiteSpace(_currentContext.UpkPath))
            folder = Path.GetDirectoryName(_currentContext.UpkPath);

        if (string.IsNullOrWhiteSpace(folder))
        {
            IReadOnlyList<RecentUpkEntry> recent = RecentUpkSession.GetRecentEntries();
            folder = recent.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.UpkPath)) is { } entry
                ? Path.GetDirectoryName(entry.UpkPath)
                : null;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            BackupStatusText.Text = "No recent UPK folder is available yet.";
            return;
        }

        UseFolder(folder, remember: true);
        await ScanBackupsAsync().ConfigureAwait(true);
    }

    private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        UseFolder(folder.Path, remember: true);
    }

    private async void ScanBackupsButton_Click(object sender, RoutedEventArgs e)
    {
        await ScanBackupsAsync().ConfigureAwait(true);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ScanBackupsAsync().ConfigureAwait(true);
    }

    private void RestoreSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupListView.SelectedItem is not BackupEntryViewModel selected)
        {
            BackupStatusText.Text = "Select a backup entry first.";
            return;
        }

        try
        {
            SetBusy(true);
            BackupStatusText.Text = "Restoring backup...";
            _service.RestoreBackup(selected.Entry);
            BackupStatusText.Text = $"Restored {selected.FileName}.";
            DetailsListView.ItemsSource = BuildDetails(selected.Entry);
        }
        catch (Exception ex)
        {
            BackupStatusText.Text = $"Backup restore failed while applying the selected entry: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RestoreLastBackupFolderAsync()
    {
        string? folder = BackupWorkspaceSession.LastFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        UseFolder(folder, remember: false);
        RecursiveCheckBox.IsChecked = BackupWorkspaceSession.Recursive;
        await ScanBackupsAsync().ConfigureAwait(true);
    }

    private Task ScanBackupsAsync()
    {
        if (_busy)
            return Task.CompletedTask;

        string folder = FolderPathTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            BackupStatusText.Text = "Select a valid folder first.";
            return Task.CompletedTask;
        }

        try
        {
            SetBusy(true);
            BackupStatusText.Text = "Scanning for backups...";
            List<BackupEntry> entries = _service.ScanBackupFiles(folder, RecursiveCheckBox.IsChecked == true);
            BackupEntries.Clear();
            foreach (BackupEntry entry in entries)
                BackupEntries.Add(new BackupEntryViewModel(entry));

            BackupSummaryText.Text = BackupEntries.Count == 0
                ? "No .bak files were found in that folder."
                : $"Found {BackupEntries.Count:N0} backup file(s).";
            BackupTitleText.Text = BackupEntries.Count == 0
                ? "No backups found."
                : $"{BackupEntries.Count:N0} backup file(s) found.";
            BackupStatusText.Text = $"Scanned {folder}.";
            DetailsListView.ItemsSource = BackupEntries.Count > 0
                ? BuildDetails(BackupEntries[0].Entry)
                : BuildDetails(null);

            if (BackupEntries.Count > 0)
                BackupListView.SelectedIndex = 0;

            BackupWorkspaceSession.Remember(folder, RecursiveCheckBox.IsChecked == true);
        }
        catch (Exception ex)
        {
            BackupStatusText.Text = $"Backup scan failed while reading {folder}: {ex.Message}";
            BackupSummaryText.Text = "Unable to scan the selected folder.";
        }
        finally
        {
            SetBusy(false);
        }

        return Task.CompletedTask;
    }

    private void BackupListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupListView.SelectedItem is not BackupEntryViewModel selected)
        {
            DetailsListView.ItemsSource = BuildDetails(null);
            return;
        }

        DetailsListView.ItemsSource = BuildDetails(selected.Entry);
        BackupStatusText.Text = $"Selected {selected.BackupFileName}.";
    }

    private void UseFolder(string? folder, bool remember)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        _currentFolder = folder;
        FolderPathTextBox.Text = folder;
        FolderStatusText.Text = $"Folder: {folder}";
        if (remember)
            BackupWorkspaceSession.Remember(folder, RecursiveCheckBox.IsChecked == true);
    }

    private void SetEmptyState()
    {
        BackupTitleText.Text = "No backups scanned yet.";
        BackupStatusText.Text = "Choose a folder and scan for .bak files.";
        BackupSummaryText.Text = "Backup details will appear here once results are scanned.";
        FolderStatusText.Text = "No folder selected.";
        DetailsListView.ItemsSource = BuildDetails(null);
        BackupEntries.Clear();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BackupListView.IsEnabled = !busy;
    }

    private static List<string> BuildDetails(BackupEntry? entry)
    {
        List<string> rows =
        [
            "Backup details",
            "Use this page to scan .bak files next to a package and restore the selected copy."
        ];

        if (entry is null)
            return rows;

        rows.Add(string.Empty);
        rows.Add($"File: {entry.FileName}");
        rows.Add($"Backup File: {entry.BackupFileName}");
        rows.Add($"Backup Path: {entry.BackupPath}");
        rows.Add($"Original Path: {entry.OriginalPath}");
        rows.Add($"Original Exists: {entry.OriginalExists}");
        rows.Add($"Backup Size: {FormatSize(entry.BackupSizeBytes)}");
        rows.Add($"Original Size: {(entry.OriginalSizeBytes.HasValue ? FormatSize(entry.OriginalSizeBytes.Value) : "<missing>")}");
        rows.Add($"Modified: {entry.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        return rows;
    }

    private static string FormatSize(long sizeBytes)
    {
        if (sizeBytes < 1024)
            return $"{sizeBytes:N0} B";
        if (sizeBytes < 1024 * 1024)
            return $"{sizeBytes / 1024.0:0.##} KB";
        return $"{sizeBytes / 1024.0 / 1024.0:0.##} MB";
    }

    public sealed class BackupEntryViewModel
    {
        public BackupEntryViewModel(BackupEntry entry)
        {
            Entry = entry;
        }

        public BackupEntry Entry { get; }
        public string FileName => Entry.FileName;
        public string BackupFileName => Entry.BackupFileName;
        public string BackupPath => Entry.BackupPath;
        public string OriginalPath => Entry.OriginalPath;
        public string LastWriteTimeText => $"Modified: {Entry.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    }
}

