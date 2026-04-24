using OmegaAssetStudio.WinUI.OmegaIntel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class OmegaIntelPage : Page
{
    private readonly OmegaIntelAnalyzerService analyzerService = new();
    private readonly OmegaIntelReportService reportService = new();
    private readonly OIEService oieService = new();
    private readonly OmegaIntelViewModel viewModel = new();
    private OmegaIntelScanResult? currentResult;
    private OmegaIntelFileRecord? selectedRecord;
    private string migrationReadinessUpkPath = string.Empty;
    private string migrationReadinessOutputPath = OmegaIntelPaths.MigrationReadinessDirectory;

    public ObservableCollection<OmegaIntelFileRecord> ScanRows { get; } = [];
    public ObservableCollection<string> InspectorRows { get; } = [];
    public ObservableCollection<string> EntityInspectorRows { get; } = [];
    public ObservableCollection<OmegaIntelGraphNode> GraphNodes { get; } = [];
    public ObservableCollection<OmegaIntelGraphNode> VisibleGraphNodes { get; } = [];
    public ObservableCollection<string> GraphRows { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];
    private OmegaIntelGraphNode? selectedEntity;
    private string scanSearchText = string.Empty;
    private bool scanUpkOnly;
    private string graphSearchText = string.Empty;
    private string graphKindFilter = "All";
    private bool suppressGraphSelectionUpdates;
    private bool suppressSessionWrites;

    public OmegaIntelPage()
    {
        InitializeComponent();
        viewModel.ScanUpkRequestedAsync = ScanUpkAsync;
        DataContext = viewModel;
        NavigationCacheMode = NavigationCacheMode.Required;

        ScanResultsListView.ItemsSource = ScanRows;
        ScanFilterStatusTextBlock.Text = "Showing all scanned files.";
        InspectorListView.ItemsSource = InspectorRows;
        EntityInspectorListView.ItemsSource = EntityInspectorRows;
        EntitySelectorComboBox.ItemsSource = VisibleGraphNodes;
        GraphKindFilterComboBox.ItemsSource = new[] { "All" };
        GraphKindFilterComboBox.SelectedIndex = 0;
        GraphListView.ItemsSource = GraphRows;
        LogListView.ItemsSource = LogEntries;

        suppressSessionWrites = true;
        ApplySessionState();
        suppressSessionWrites = false;

        FoldersTextBlock.Text = $"Logs: {OmegaIntelPaths.LogsDirectory} | Reports: {OmegaIntelPaths.ReportsDirectory} | Cache: {OmegaIntelPaths.CacheDirectory} | Migration Readiness: {OmegaIntelPaths.MigrationReadinessDirectory}";
        SummaryTextBlock.Text = "Status: waiting for a game folder.";
        ValidationTextBlock.Text = "Validation: not run.";
        ProgressTextBlock.Text = "Progress: idle";
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Value = 0;
        FilesCountTextBlock.Text = "Files: 0";
        UpkCountTextBlock.Text = "UPKs: 0";
        TfcCountTextBlock.Text = "TFCs: 0";
        ManifestCountTextBlock.Text = "TFC manifests: 0";
        TextureCountTextBlock.Text = "Textures: 0";
        MeshCountTextBlock.Text = "Meshes: 0";
        AnimCountTextBlock.Text = "Animations: 0";
        UiCountTextBlock.Text = "UI-heavy UPKs: 0";
        CharacterCountTextBlock.Text = "Character-heavy UPKs: 0";
        ResolvedCountTextBlock.Text = "Resolved-object UPKs: 0";
        CacheBackedCountTextBlock.Text = "Cache-backed texture UPKs: 0";
        DirectoryCountTextBlock.Text = "Directories: 0";
        EntropyCountTextBlock.Text = "High-entropy files: 0";
        ExeCountTextBlock.Text = "Executables: 0";
        LogEntries.Add("Omega Intel is ready.");

        AddLog("Omega Intelligence Engine ready.");
        RefreshInspector(null);
        MigrationReadinessOutputBox.Text = migrationReadinessOutputPath;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AddLog("Navigated to Omega Intelligence Engine.");
        if (string.IsNullOrWhiteSpace(RootPathBox.Text) && !string.IsNullOrWhiteSpace(WorkspaceSessionStore.OmegaIntel.RootPath))
            RootPathBox.Text = WorkspaceSessionStore.OmegaIntel.RootPath;
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

    private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        RootPathBox.Text = folder.Path;
        AddLog($"Folder selected: {folder.Path}");
        SaveSessionState();
    }

    private async Task ScanUpkAsync()
    {
        try
        {
            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add(".upk");
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            InitializePicker(picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            SetBusy("Scanning UPK...");
            SetProgress(true, "Scanning selected UPK...");
            AddLog($"UPK selected: {file.Path}");

            currentResult = await oieService.StartScan(file.Path, AddLog);
            ApplyScanResult(currentResult);
            AddLog($"UPK scan completed: {currentResult.Summary}");
            SetProgress(false, "UPK scan complete.");
            SetBusy("Ready.");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AddLog($"UPK scan failed: {ex}");
            SetProgress(false, "UPK scan failed.");
            SetBusy("UPK scan failed.");
        }
    }

    private async void BrowseMigrationReadinessUpkButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add(".upk");
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            InitializePicker(picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            migrationReadinessUpkPath = file.Path;
            MigrationReadinessUpkBox.Text = migrationReadinessUpkPath;
            MigrationReadinessStatusTextBlock.Text = $"Readiness UPK selected: {Path.GetFileName(file.Path)}";
            AddLog($"Migration readiness UPK selected: {file.Path}");
        }
        catch (Exception ex)
        {
            AddLog($"Migration readiness UPK selection failed: {ex.Message}");
            App.WriteDiagnosticsLog("OmegaIntel.MigrationReadinessSelectUpk", ex.ToString());
        }
    }

    private async void BrowseMigrationReadinessOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FolderPicker picker = new();
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            InitializePicker(picker);
            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is null)
                return;

            migrationReadinessOutputPath = folder.Path;
            MigrationReadinessOutputBox.Text = migrationReadinessOutputPath;
            MigrationReadinessStatusTextBlock.Text = $"Readiness output folder selected: {folder.Path}";
            AddLog($"Migration readiness output folder selected: {folder.Path}");
        }
        catch (Exception ex)
        {
            AddLog($"Migration readiness output folder selection failed: {ex.Message}");
            App.WriteDiagnosticsLog("OmegaIntel.MigrationReadinessSelectOutput", ex.ToString());
        }
    }

    private async void ExportMigrationReadinessButton_Click(object sender, RoutedEventArgs e)
    {
        string upkPath = MigrationReadinessUpkBox.Text?.Trim() ?? string.Empty;
        string outputPath = MigrationReadinessOutputBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(upkPath))
        {
            AddLog("Choose a single UPK before exporting a migration readiness report.");
            MigrationReadinessStatusTextBlock.Text = "Readiness export: waiting for a UPK.";
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = OmegaIntelPaths.MigrationReadinessDirectory;

        try
        {
            SetBusy("Scanning migration readiness UPK...");
            SetProgress(true, "Scanning migration readiness UPK...");
            AddLog($"Migration readiness scan started: {upkPath}");

            currentResult = await oieService.StartScan(upkPath, AddLog);
            ApplyScanResult(currentResult);

            MigrationReadinessStatusTextBlock.Text = "Readiness scan complete. Exporting report...";
            AddLog($"Migration readiness scan completed: {currentResult.Summary}");

            string writtenDirectory = await reportService.ExportMigrationReadinessAsync(currentResult, upkPath, outputPath);
            MigrationReadinessStatusTextBlock.Text = $"Readiness report written to: {writtenDirectory}";
            AddLog($"Migration readiness report written to {writtenDirectory}");

            SetProgress(false, "Migration readiness export complete.");
            SetBusy("Ready.");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AddLog($"Migration readiness export failed: {ex}");
            MigrationReadinessStatusTextBlock.Text = "Readiness export failed.";
            SetProgress(false, "Migration readiness export failed.");
            SetBusy("Readiness export failed.");
            App.WriteDiagnosticsLog("OmegaIntel.MigrationReadiness", ex.ToString());
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync("Scan started", options =>
        {
            options.DeepUpkAnalysis = false;
            options.AnalyzeExecutables = true;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = true;
            options.AnalyzeTextureFiles = true;
            options.AnalyzeMeshFiles = true;
            options.AnalyzeAnimationFiles = true;
            options.AnalyzeCharacterFiles = true;
            options.AnalyzeUiFiles = true;
            options.BuildKnowledgeGraph = false;
        });
    }

    private async void RunFileSystemAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("File system analysis started", options =>
        {
            options.DeepUpkAnalysis = false;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = false;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = false;
        });

    private async void RunUpkAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("UPK analysis started", options =>
        {
            options.DeepUpkAnalysis = true;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunTfcAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("TFC analysis started", options =>
        {
            options.DeepUpkAnalysis = false;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = false;
            options.AnalyzeTfcFiles = true;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunTextureAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("Texture analysis started", options =>
        {
            options.DeepUpkAnalysis = true;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = true;
            options.AnalyzeTextureFiles = true;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunSkeletonAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("Skeleton analysis started", options =>
        {
            options.DeepUpkAnalysis = true;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = true;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunAnimationAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("Animation analysis started", options =>
        {
            options.DeepUpkAnalysis = true;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = true;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunCharacterAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("CharacterDefinition analysis started", options =>
        {
            options.DeepUpkAnalysis = true;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = true;
            options.AnalyzeUiFiles = true;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunUiAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("UI analysis started", options =>
        {
            options.DeepUpkAnalysis = true;
            options.AnalyzeExecutables = false;
            options.AnalyzeUpkFiles = true;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = true;
            options.BuildKnowledgeGraph = true;
        });

    private async void RunExecutableAnalysisButton_Click(object sender, RoutedEventArgs e) =>
        await RunScanAsync("Executable analysis started", options =>
        {
            options.DeepUpkAnalysis = false;
            options.AnalyzeExecutables = true;
            options.AnalyzeUpkFiles = false;
            options.AnalyzeTfcFiles = false;
            options.AnalyzeTextureFiles = false;
            options.AnalyzeMeshFiles = false;
            options.AnalyzeAnimationFiles = false;
            options.AnalyzeCharacterFiles = false;
            options.AnalyzeUiFiles = false;
            options.BuildKnowledgeGraph = true;
        });

    private void BuildKnowledgeGraphButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null)
        {
            AddLog("Run a scan before building the knowledge graph.");
            return;
        }

        try
        {
            AddLog("Building knowledge graph from the current scan...");
            new OmegaIntelKnowledgeGraphService().BuildGraph(currentResult);
            UpdateSummary(currentResult);
            AddLog($"Knowledge graph rebuilt from the current scan: nodes={currentResult.Nodes.Count:N0}, edges={currentResult.Edges.Count:N0}");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AddLog($"Knowledge graph build failed while rebuilding the current scan graph: {ex.Message}");
            App.WriteDiagnosticsLog("OmegaIntel.KnowledgeGraph", ex.ToString());
        }
    }

    private async Task RunScanAsync(string startMessage, Action<OmegaIntelScanOptions> configure)
    {
        string rootPath = RootPathBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            AddLog("Choose a game folder before scanning.");
            return;
        }

        try
        {
            SetBusy("Scanning...");
            SetProgress(true, "Starting scan...");
            AddLog($"{startMessage}: {rootPath}");

            OmegaIntelScanOptions options = new()
            {
                RootPath = rootPath
            };
            configure(options);

            var result = await analyzerService.ScanAsync(options, AddLog);

            currentResult = result;
            ApplyScanResult(result);
            AddLog($"Scan completed: {result.Summary}");
            SetProgress(false, "Scan complete.");
            SetBusy("Ready.");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AddLog($"Scan failed: {ex}");
            SetProgress(false, "Scan failed.");
            SetBusy("Scan failed.");
        }
    }

    private void ApplyScanResult(OmegaIntelScanResult result)
    {
        try
        {
            PopulateScanRows(result);
        }
        catch (Exception ex)
        {
            AddLog($"UI update failed while populating scan rows: {ex}");
        }

        try
        {
            UpdateSummary(result);
        }
        catch (Exception ex)
        {
            AddLog($"UI update failed while updating summary: {ex}");
        }

        try
        {
            RefreshInspector(ScanRows.FirstOrDefault());
        }
        catch (Exception ex)
        {
            AddLog($"UI update failed while refreshing inspector: {ex}");
        }
    }

    private async void ExportReportsButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null)
        {
            AddLog("Run a scan before exporting reports.");
            return;
        }

        try
        {
            AddLog("Exporting reports...");
            await reportService.ExportAllAsync(currentResult);
            AddLog($"Reports written to {OmegaIntelPaths.ReportsDirectory}");
        }
        catch (Exception ex)
        {
            AddLog($"Report export failed while writing the Omega Intel outputs: {ex.Message}");
            App.WriteDiagnosticsLog("OmegaIntel.Export", ex.ToString());
        }
    }

    private void OpenOmegaIntelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string omegaRoot = Path.Combine(AppContext.BaseDirectory, "OmegaIntel");
            Directory.CreateDirectory(omegaRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{omegaRoot}\"",
                UseShellExecute = true
            });
            AddLog("Opened OmegaIntel output folder.");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to open the OmegaIntel output folder: {ex.Message}");
            App.WriteDiagnosticsLog("OmegaIntel.OpenFolder", ex.ToString());
        }
    }

    private void OpenReportsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(OmegaIntelPaths.ReportsDirectory, "reports");
    }

    private void OpenLatestMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null)
        {
            AddLog("Run a scan before opening the latest report.");
            return;
        }

        OpenFileInExplorer(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{currentResult.ReportStamp}.md"), "latest markdown report");
    }

    private void OpenLatestHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null)
        {
            AddLog("Run a scan before opening the latest report.");
            return;
        }

        OpenFileInExplorer(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{currentResult.ReportStamp}.html"), "latest HTML report");
    }

    private void OpenLatestJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null)
        {
            AddLog("Run a scan before opening the latest report.");
            return;
        }

        OpenFileInExplorer(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{currentResult.ReportStamp}.json"), "latest JSON report");
    }

    private void OpenLatestCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null)
        {
            AddLog("Run a scan before opening the latest report.");
            return;
        }

        OpenFileInExplorer(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{currentResult.ReportStamp}.csv"), "latest CSV report");
    }

    private void ScanResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedRecord = ScanResultsListView.SelectedItem as OmegaIntelFileRecord;
        RefreshInspector(selectedRecord);
    }

    private void ScanSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        scanSearchText = ScanSearchBox.Text?.Trim() ?? string.Empty;
        RefreshVisibleScanRows();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void ScanUpkOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        scanUpkOnly = ScanUpkOnlyCheckBox.IsChecked == true;
        RefreshVisibleScanRows();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void EntitySelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedEntity = EntitySelectorComboBox.SelectedItem as OmegaIntelGraphNode;
        RefreshEntityInspector(selectedEntity);
    }

    private void EntitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        graphSearchText = EntitySearchBox.Text?.Trim() ?? string.Empty;
        RefreshVisibleGraphNodes();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void GraphKindFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        graphKindFilter = GraphKindFilterComboBox.SelectedItem?.ToString() ?? "All";
        RefreshVisibleGraphNodes();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void PopulateScanRows(OmegaIntelScanResult result)
    {
        ScanRows.Clear();
        foreach (var item in result.Files.OrderByDescending(item => item.Kind).ThenBy(item => item.Name))
            ScanRows.Add(item);

        RefreshVisibleScanRows();
    }

    private void UpdateSummary(OmegaIntelScanResult result)
    {
        FilesCountTextBlock.Text = $"Files: {result.TotalFiles:N0}";
        UpkCountTextBlock.Text = $"UPKs: {result.UpkFiles:N0}";
        TfcCountTextBlock.Text = $"TFCs: {result.TfcFiles:N0}";
        ManifestCountTextBlock.Text = $"TFC manifests: {result.TfcManifestFiles:N0}";
        TextureCountTextBlock.Text = $"Textures: {result.TextureFiles:N0}";
        MeshCountTextBlock.Text = $"Meshes: {result.MeshFiles:N0}";
        AnimCountTextBlock.Text = $"Animations: {result.AnimationFiles:N0}";
        ResolvedCountTextBlock.Text = $"Resolved-object UPKs: {result.ResolvedTextureUpks + result.ResolvedMeshUpks + result.ResolvedAnimSetUpks + result.ResolvedCharacterUpks:N0}";
        CacheBackedCountTextBlock.Text = $"Cache-backed texture UPKs: {result.CacheBackedTextureUpks:N0}";
        DirectoryCountTextBlock.Text = $"Directories: {result.DirectoryCount:N0}";
        EntropyCountTextBlock.Text = $"High-entropy files: {result.HighEntropyFiles:N0} / unknown magic: {result.UnknownMagicFiles:N0}";
        UiCountTextBlock.Text = $"UI-heavy UPKs: {result.UiLikeUpks:N0} | UI hero entries: {result.UiHeroEntries:N0}";
        CharacterCountTextBlock.Text = $"Character-heavy UPKs: {result.CharacterLikeUpks:N0} | hero IDs: {result.HeroIdCandidates:N0}";
        ExeCountTextBlock.Text = $"Executables: {result.ExecutableFiles:N0} | roster: {result.RosterTableCandidates:N0} | power: {result.PowerTreeCandidates:N0} | strings: {result.StringTableEntries:N0} | signatures: {result.FunctionSignatureEntries:N0}";
        SummaryTextBlock.Text = result.Summary ?? "Status: ready.";
        ValidationTextBlock.Text = result.ValidationIssues == 0
            ? "Validation: clean."
            : $"Validation: {result.ValidationErrors:N0} error(s), {result.ValidationWarnings:N0} warning(s), {result.ValidationIssues:N0} issue(s).";

        GraphRows.Clear();
        GraphNodes.Clear();
        VisibleGraphNodes.Clear();
        GraphRows.Add($"Nodes: {result.Nodes.Count:N0}");
        GraphRows.Add($"Edges: {result.Edges.Count:N0}");
        GraphRows.Add($"Classified files: {result.ClassifiedFiles:N0}");
        foreach (var node in result.Nodes.OrderByDescending(node => node.Weight).ThenBy(node => node.Label))
            GraphNodes.Add(node);

        suppressGraphSelectionUpdates = true;
        try
        {
            GraphKindFilterComboBox.ItemsSource = GraphNodes
                .Select(node => node.Kind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(kind => kind)
                .Prepend("All")
                .ToArray();
            GraphKindFilterComboBox.SelectedItem = "All";
            RefreshVisibleGraphNodes();
        }
        finally
        {
            suppressGraphSelectionUpdates = false;
        }

        foreach (var node in result.Nodes.OrderByDescending(node => node.Weight).ThenBy(node => node.Label).Take(20))
        {
            GraphRows.Add($"{node.Kind}: {node.Label}");
        }
    }

    private void RefreshInspector(OmegaIntelFileRecord? record)
    {
        InspectorRows.Clear();

        if (record is null)
        {
            SelectedPathTextBlock.Text = "Select a file to inspect it here.";
            InspectorRows.Add("No file selected.");
            return;
        }

        SelectedPathTextBlock.Text = record.Path;
        InspectorRows.Add($"Name: {record.Name}");
        InspectorRows.Add($"Kind: {record.Kind}");
        InspectorRows.Add($"Classification: {record.Classification}");
        InspectorRows.Add($"Summary: {record.Summary}");
        InspectorRows.Add($"Size: {FormatBytes(record.SizeBytes)}");
        InspectorRows.Add($"Modified: {record.LastWriteTimeUtc:O}");
        InspectorRows.Add($"Extension: {record.Extension}");
        InspectorRows.Add($"Directory: {record.Directory}");
        InspectorRows.Add($"Magic: {record.MagicBytes}");
        InspectorRows.Add($"Entropy: {record.Entropy:F2}");
        InspectorRows.Add($"Tags: {string.Join(", ", record.Tags)}");
        InspectorRows.Add($"Insights: {record.Insights.Count:N0}");
        InspectorRows.Add(string.IsNullOrWhiteSpace(record.Details) ? "Details: (none)" : $"Details: {record.Details}");

        foreach (var insight in record.Insights.Take(12))
        {
            InspectorRows.Add($"{insight.Kind}: {insight.Value}");
        }
    }

    private void RefreshEntityInspector(OmegaIntelGraphNode? node)
    {
        EntityInspectorRows.Clear();

        if (node is null)
        {
            EntityInspectorRows.Add("No entity selected.");
            return;
        }

        EntityInspectorRows.Add($"Id: {node.Id}");
        EntityInspectorRows.Add($"Kind: {node.Kind}");
        EntityInspectorRows.Add($"Label: {node.Label}");
        EntityInspectorRows.Add($"Source: {node.SourcePath ?? "(none)"}");
        EntityInspectorRows.Add($"Description: {node.Description ?? "(none)"}");
        EntityInspectorRows.Add($"Weight: {node.Weight:0.00}");

        if (currentResult is null)
            return;

        var outgoing = currentResult.Edges.Where(edge => string.Equals(edge.FromId, node.Id, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
        var incoming = currentResult.Edges.Where(edge => string.Equals(edge.ToId, node.Id, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();

        EntityInspectorRows.Add($"Outgoing: {outgoing.Count:N0}");
        foreach (var edge in outgoing)
            EntityInspectorRows.Add($"-> {edge.Label}: {edge.ToId}");

        EntityInspectorRows.Add($"Incoming: {incoming.Count:N0}");
        foreach (var edge in incoming)
            EntityInspectorRows.Add($"<- {edge.Label}: {edge.FromId}");
    }

    private void RefreshVisibleGraphNodes()
    {
        VisibleGraphNodes.Clear();

        IEnumerable<OmegaIntelGraphNode> query = GraphNodes;
        if (!string.Equals(graphKindFilter, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(node => string.Equals(node.Kind, graphKindFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(graphSearchText))
        {
            query = query.Where(node =>
                node.Label.Contains(graphSearchText, StringComparison.OrdinalIgnoreCase) ||
                node.Kind.Contains(graphSearchText, StringComparison.OrdinalIgnoreCase) ||
                (node.Description?.Contains(graphSearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var node in query.OrderByDescending(node => node.Weight).ThenBy(node => node.Label))
            VisibleGraphNodes.Add(node);

        if (!suppressGraphSelectionUpdates)
            EntitySelectorComboBox.SelectedItem = VisibleGraphNodes.FirstOrDefault();
    }

    private void RefreshVisibleScanRows()
    {
        if (ScanResultsListView is null || ScanFilterStatusTextBlock is null)
            return;

        IEnumerable<OmegaIntelFileRecord> query = ScanRows;

        if (scanUpkOnly)
            query = query.Where(item => item.Kind == OmegaIntelFileKind.Upk);

        if (!string.IsNullOrWhiteSpace(scanSearchText))
        {
            query = query.Where(item =>
                item.Name.Contains(scanSearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(scanSearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Classification.Contains(scanSearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Summary.Contains(scanSearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(tag => tag.Contains(scanSearchText, StringComparison.OrdinalIgnoreCase)));
        }

        var visible = query.OrderBy(item => item.Name).ToList();
        ScanResultsListView.ItemsSource = visible;
        ScanResultsListView.SelectedItem = visible.FirstOrDefault();

        if (currentResult is null)
        {
            ScanFilterStatusTextBlock.Text = "Showing all scanned files.";
            return;
        }

        ScanFilterStatusTextBlock.Text = scanUpkOnly
            ? $"Showing {visible.Count:N0} of {ScanRows.Count:N0} scanned files. UPK-only filter is on."
            : string.IsNullOrWhiteSpace(scanSearchText)
                ? $"Showing all {visible.Count:N0} scanned files."
                : $"Showing {visible.Count:N0} scanned files matching \"{scanSearchText}\".";
    }

    private void ApplySessionState()
    {
        var session = WorkspaceSessionStore.OmegaIntel;
        if (!string.IsNullOrWhiteSpace(session.RootPath))
            RootPathBox.Text = session.RootPath;
        if (!string.IsNullOrWhiteSpace(session.ScanSearchText))
            ScanSearchBox.Text = session.ScanSearchText;
        ScanUpkOnlyCheckBox.IsChecked = session.ScanUpkOnly;
        if (!string.IsNullOrWhiteSpace(session.GraphSearchText))
            EntitySearchBox.Text = session.GraphSearchText;
        if (!string.IsNullOrWhiteSpace(session.GraphKindFilter))
            graphKindFilter = session.GraphKindFilter;
    }

    private void SaveSessionState()
    {
        if (suppressSessionWrites)
            return;

        WorkspaceSessionStore.RememberOmegaIntel(new WorkspaceSessionStore.OmegaIntelWorkspaceSession
        {
            RootPath = RootPathBox.Text?.Trim() ?? string.Empty,
            ScanSearchText = ScanSearchBox.Text?.Trim() ?? string.Empty,
            ScanUpkOnly = ScanUpkOnlyCheckBox.IsChecked == true,
            GraphSearchText = EntitySearchBox.Text?.Trim() ?? string.Empty,
            GraphKindFilter = GraphKindFilterComboBox.SelectedItem?.ToString() ?? graphKindFilter
        });
    }

    private void AddLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        App.WriteDiagnosticsLog("OmegaIntel", message);
        try
        {
            File.AppendAllText(OmegaIntelPaths.AnalysisLogPath, line + Environment.NewLine);
        }
        catch
        {
        }

        void Append()
        {
            LogEntries.Insert(0, line);
            while (LogEntries.Count > 50)
                LogEntries.RemoveAt(LogEntries.Count - 1);
            StatusTextBlock.Text = $"Status: {message}";
        }

        if (DispatcherQueue.HasThreadAccess)
            Append();
        else
            DispatcherQueue.TryEnqueue(Append);
    }

    private void SetBusy(string message)
    {
        StatusTextBlock.Text = $"Status: {message}";
    }

    private void SetProgress(bool indeterminate, string message)
    {
        ScanProgressBar.IsIndeterminate = indeterminate;
        if (indeterminate)
            ScanProgressBar.Value = 0;

        ProgressTextBlock.Text = $"Progress: {message}";
    }

    private void OpenFolderInExplorer(string folderPath, string label)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
            AddLog($"Opened {label} folder.");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to open the {label} folder: {ex.Message}");
        }
    }

    private void OpenFileInExplorer(string filePath, string label)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                AddLog($"No {label} exists yet.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
            AddLog($"Opened {label}.");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to open the {label}: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }
}

