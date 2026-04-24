using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.ThanosMigration.Services;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class ThanosPrototypeMergerViewModel : INotifyPropertyChanged
{
    private readonly ThanosPrototypeDiscoveryService discoveryService;
    private readonly ThanosPrototypeMergePlanner mergePlanner;
    private readonly ThanosPrototypeMergerService mergerService;
    private readonly ThanosDependencyScannerService dependencyScannerService;
    private readonly UpkFileRepository upkRepository = new();
    private ThanosDependencyReport? selectedReport;
    private string? client148Root;
    private string? client152Root;
    private bool isMerging;
    private bool isDiscovering;
    private bool showOnlyRaidRelevant = true;
    private string statusText = "Ready.";
    private double discoveryProgressValue;
    private double discoveryProgressMaximum = 100.0;
    private string discoveryCurrentFile = string.Empty;
    private string discoveryStatus = string.Empty;
    private IReadOnlyList<ThanosPrototypeSource> lastDiscoveryResults = [];
    private readonly AsyncRelayCommand loadReportCommand;
    private readonly AsyncRelayCommand discoverPrototypesCommand;
    private readonly AsyncRelayCommand buildMergePlansCommand;
    private readonly AsyncRelayCommand runMergeCommand;
    private readonly AsyncRelayCommand browseClient148RootCommand;
    private readonly AsyncRelayCommand browseClient152RootCommand;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Func<Task<string?>>? BrowseReportRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseClient148RootRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseClient152RootRequestedAsync { get; set; }

    public IAsyncRelayCommand LoadReportCommand => loadReportCommand;

    public IAsyncRelayCommand DiscoverPrototypesCommand => discoverPrototypesCommand;

    public IAsyncRelayCommand BuildMergePlansCommand => buildMergePlansCommand;

    public IAsyncRelayCommand RunMergeCommand => runMergeCommand;

    public IAsyncRelayCommand BrowseClient148RootCommand => browseClient148RootCommand;

    public IAsyncRelayCommand BrowseClient152RootCommand => browseClient152RootCommand;

    public ObservableCollection<ThanosPrototypeSource> DiscoveredPrototypes { get; } = [];

    public ObservableCollection<ThanosPrototypeMergePlan> MergePlans { get; } = [];

    public ObservableCollection<ThanosMigrationStep> MergeSteps { get; } = [];

    public ThanosDependencyReport? SelectedReport
    {
        get => selectedReport;
        set
        {
            if (SetField(ref selectedReport, value))
            {
                MergePlans.Clear();
                DiscoveredPrototypes.Clear();
                MergeSteps.Clear();
                StatusText = value is null ? "Ready." : $"Loaded report: {Path.GetFileName(value.FilePath)}";
                RefreshCommandStates();
            }
        }
    }

    public string? Client148Root
    {
        get => client148Root;
        set
        {
            if (SetField(ref client148Root, value))
            {
                PrototypeMergerSessionStore.Remember(client148Root: client148Root);
                RefreshCommandStates();
            }
        }
    }

    public string? Client152Root
    {
        get => client152Root;
        set
        {
            if (SetField(ref client152Root, value))
            {
                PrototypeMergerSessionStore.Remember(client152Root: client152Root);
                RefreshCommandStates();
            }
        }
    }

    public bool IsMerging
    {
        get => isMerging;
        private set
        {
            if (SetField(ref isMerging, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RefreshCommandStates();
            }
        }
    }

    public bool IsDiscovering
    {
        get => isDiscovering;
        private set
        {
            if (SetField(ref isDiscovering, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RefreshCommandStates();
            }
        }
    }

    public bool IsBusy => IsMerging || IsDiscovering;

    public double DiscoveryProgressValue
    {
        get => discoveryProgressValue;
        private set => SetField(ref discoveryProgressValue, value);
    }

    public double DiscoveryProgressMaximum
    {
        get => discoveryProgressMaximum;
        private set => SetField(ref discoveryProgressMaximum, value);
    }

    public string DiscoveryCurrentFile
    {
        get => discoveryCurrentFile;
        private set => SetField(ref discoveryCurrentFile, value);
    }

    public string DiscoveryStatus
    {
        get => discoveryStatus;
        private set => SetField(ref discoveryStatus, value);
    }

    public bool ShowOnlyRaidRelevant
    {
        get => showOnlyRaidRelevant;
        set
        {
            if (SetField(ref showOnlyRaidRelevant, value))
                ApplyDiscoveryFilter();
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public ThanosPrototypeMergerViewModel(
        ThanosPrototypeDiscoveryService discoveryService,
        ThanosPrototypeMergePlanner mergePlanner,
        ThanosPrototypeMergerService mergerService)
    {
        this.discoveryService = discoveryService;
        this.mergePlanner = mergePlanner;
        this.mergerService = mergerService;
        dependencyScannerService = new ThanosDependencyScannerService(upkRepository);

        PrototypeMergerSessionStore.PrototypeMergerSessionData savedSession = PrototypeMergerSessionStore.Load();
        client148Root = savedSession.Client148Root;
        client152Root = savedSession.Client152Root;

        loadReportCommand = new AsyncRelayCommand(LoadReportAsync);
        discoverPrototypesCommand = new AsyncRelayCommand(DiscoverPrototypesAsync, CanDiscover);
        buildMergePlansCommand = new AsyncRelayCommand(BuildMergePlansAsync, CanBuildPlans);
        runMergeCommand = new AsyncRelayCommand(RunMergeAsync, CanRunMerge);
        browseClient148RootCommand = new AsyncRelayCommand(BrowseClient148RootAsync);
        browseClient152RootCommand = new AsyncRelayCommand(BrowseClient152RootAsync);
    }

    public async Task LoadReportAsync()
    {
        if (BrowseReportRequestedAsync is null)
        {
            StatusText = "No report picker is configured.";
            return;
        }

        string? path = await BrowseReportRequestedAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "No report was selected.";
            return;
        }

        string extension = Path.GetExtension(path);
        if (extension.Equals(".upk", StringComparison.OrdinalIgnoreCase))
        {
            await LoadReportFromUpkAsync(path).ConfigureAwait(true);
            return;
        }

        await LoadReportFromPathAsync(path).ConfigureAwait(true);
    }

    public async Task LoadReportFromPathAsync(string path)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            ThanosDependencyReport? report = JsonSerializer.Deserialize<ThanosDependencyReport>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (report is null)
            {
                StatusText = "Dependency report could not be loaded.";
                return;
            }

            report.FilePath = Path.GetFullPath(path);
            SelectedReport = report;
            StatusText = $"Loaded dependency report with {report.MissingDependencyCount:N0} missing dependency item(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load dependency report: {ex.Message}";
        }
    }

    public async Task LoadReportFromUpkAsync(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                StatusText = "Source UPK could not be found.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Client152Root))
            {
                StatusText = "Select the 1.52 client root before scanning a source UPK.";
                return;
            }

            ThanosDependencyReport report = await dependencyScannerService.ScanDependenciesAsync(fullPath, Client152Root!).ConfigureAwait(true);

            SelectedReport = report;
            StatusText = $"Scanned source UPK and found {report.MissingDependencyCount:N0} missing dependency item(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to generate dependency report from UPK: {ex.Message}";
        }
    }

    public async Task DiscoverPrototypesAsync()
    {
        if (!CanDiscover())
        {
            StatusText = "Load a dependency report, then select the 1.48 client root for prototype discovery.";
            return;
        }

        IsDiscovering = true;
        DiscoveryProgressValue = 0;
        DiscoveryProgressMaximum = Math.Max(1, SelectedReport?.MissingDependencyCount ?? 1);
        DiscoveryCurrentFile = string.Empty;
        DiscoveryStatus = "Starting prototype discovery...";
        try
        {
            StatusText = "Discovering prototypes in 1.48...";
            DiscoveryStatus = "Scanning CookedPCConsole, MarvelGame, and Engine roots where available...";
            IProgress<ThanosDiscoveryProgress> progress = new Progress<ThanosDiscoveryProgress>(progressItem =>
            {
                DiscoveryProgressMaximum = Math.Max(1, progressItem.TotalItems);
                DiscoveryProgressValue = Math.Min(DiscoveryProgressMaximum, progressItem.ProcessedItems);
                DiscoveryCurrentFile = progressItem.CurrentFile;
                DiscoveryStatus = progressItem.Status;
                if (!string.IsNullOrWhiteSpace(progressItem.Status))
                    StatusText = progressItem.Status;
            });

            lastDiscoveryResults = await discoveryService.FindPrototypeSources(SelectedReport!, Client148Root!, progress).ConfigureAwait(true);
            ApplyDiscoveryFilter();
            MergePlans.Clear();
            MergeSteps.Clear();
            DiscoveryProgressValue = DiscoveryProgressMaximum;
            DiscoveryStatus = $"Discovered {DiscoveredPrototypes.Count:N0} prototype source(s).";
            StatusText = $"Discovered {DiscoveredPrototypes.Count:N0} prototype source(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Prototype discovery failed: {ex.Message}";
        }
        finally
        {
            IsDiscovering = false;
            RefreshCommandStates();
        }
    }

    public Task BuildMergePlansAsync()
    {
        if (!CanBuildPlans())
        {
            StatusText = "Discover prototypes and select the 1.52 client root first.";
            return Task.CompletedTask;
        }

        IsMerging = true;
        try
        {
            StatusText = "Building merge plans...";
            IReadOnlyList<ThanosPrototypeMergePlan> plans = mergePlanner.BuildMergePlans(DiscoveredPrototypes.ToArray(), Client152Root!);

            MergePlans.Clear();
            foreach (ThanosPrototypeMergePlan plan in plans)
                MergePlans.Add(plan);

            MergeSteps.Clear();
            StatusText = $"Built {MergePlans.Count:N0} merge plan(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Merge planning failed: {ex.Message}";
        }
        finally
        {
            IsMerging = false;
            RefreshCommandStates();
        }

        return Task.CompletedTask;
    }

    public async Task RunMergeAsync()
    {
        if (!CanRunMerge())
        {
            StatusText = "Build merge plans before running the merger.";
            return;
        }

        IsMerging = true;
        try
        {
            StatusText = "Running prototype merge...";
            IReadOnlyList<ThanosMigrationStep> steps = await mergerService.MergePrototypes(MergePlans.ToArray(), Client152Root!).ConfigureAwait(true);

            MergeSteps.Clear();
            foreach (ThanosMigrationStep step in steps)
                MergeSteps.Add(step);

            StatusText = $"Merge complete with {MergeSteps.Count:N0} step(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Prototype merge failed: {ex.Message}";
        }
        finally
        {
            IsMerging = false;
            RefreshCommandStates();
        }
    }

    public async Task BrowseClient148RootAsync()
    {
        if (BrowseClient148RootRequestedAsync is null)
            return;

        string? path = await BrowseClient148RootRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            Client148Root = path;
    }

    public async Task BrowseClient152RootAsync()
    {
        if (BrowseClient152RootRequestedAsync is null)
            return;

        string? path = await BrowseClient152RootRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            Client152Root = path;
    }

    private bool CanDiscover()
        => !IsMerging &&
           !IsDiscovering &&
           SelectedReport is not null &&
           !string.IsNullOrWhiteSpace(Client148Root);

    private bool CanBuildPlans()
        => !IsMerging &&
           DiscoveredPrototypes.Count > 0 &&
           !string.IsNullOrWhiteSpace(Client152Root);

    private bool CanRunMerge()
        => !IsMerging &&
           MergePlans.Count > 0 &&
           !string.IsNullOrWhiteSpace(Client152Root);

    private void RefreshCommandStates()
    {
        discoverPrototypesCommand.NotifyCanExecuteChanged();
        buildMergePlansCommand.NotifyCanExecuteChanged();
        runMergeCommand.NotifyCanExecuteChanged();
    }

    private void ApplyDiscoveryFilter()
    {
        DiscoveredPrototypes.Clear();

        foreach (ThanosPrototypeSource source in lastDiscoveryResults)
        {
            if (ShowOnlyRaidRelevant && !source.IsRaidRelevant)
                continue;

            DiscoveredPrototypes.Add(source);
        }

        OnPropertyChanged(nameof(DiscoveredPrototypes));
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncRelayCommand : IAsyncRelayCommand
    {
        private readonly Func<Task> execute;
        private readonly Func<bool>? canExecute;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter).ConfigureAwait(false);
        }

        public Task ExecuteAsync(object? parameter = null)
        {
            return execute();
        }

        public void NotifyCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public interface IAsyncRelayCommand : ICommand
{
    Task ExecuteAsync(object? parameter = null);

    void NotifyCanExecuteChanged();
}

