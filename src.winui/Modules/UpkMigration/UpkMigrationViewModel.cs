using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.ThanosMigration.Services;
using OmegaAssetStudio.TextureManager;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkMigrationViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue? dispatcherQueue;
    private readonly UpkMigrationService service;
    private string outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "OmegaAssetStudio_UpkMigration");
    private string statusText = "Ready.";
    private string progressText = "Select one or more 1.48 UPKs to begin.";
    private double overallProgress;
    private string resultsSummaryText = "No migration has run yet.";
    private string selectedJobDetails = "No job selected.";
    private string logFilter = string.Empty;
    private string textureManifestDirectory = string.Empty;
    private MigrationMode currentMode = MigrationMode.Standard;
    private ThanosMigrationReport? thanosReport;
    private MigrationJob? selectedJob;
    private MigrationLogEntry? selectedLogEntry;
    private bool isBusy;
    private bool isThanosBusy;
    private readonly ThanosStructuralMigrationService thanosStructuralService;
    private readonly ThanosTextureMigrationService thanosTextureService;
    private readonly TfcManifestService tfcManifestService;
    private readonly ThanosPrototypeMergerViewModel prototypeMerger;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MigrationJob> Jobs { get; } = [];

    public ObservableCollection<MigrationLogEntry> LogEntries { get; } = [];

    public ThanosPrototypeMergerViewModel PrototypeMerger => prototypeMerger;

    public Func<Task<IReadOnlyList<string>>>? SelectUpksRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseOutputDirectoryRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseTextureManifestDirectoryRequestedAsync { get; set; }

    public ICommand SelectUpksCommand { get; }

    public ICommand StartMigrationCommand { get; }

    public ICommand BrowseOutputDirectoryCommand { get; }

    public ICommand BrowseTextureManifestDirectoryCommand { get; }

    public ICommand AnalyzeThanosCommand { get; }

    public ICommand MigrateThanosCommand { get; }

    public ICommand UpdateTextureManifestCommand { get; }

    public MigrationJob? SelectedJob
    {
        get => selectedJob;
        set
        {
            if (ReferenceEquals(selectedJob, value))
                return;

            if (selectedJob is not null)
                selectedJob.PropertyChanged -= SelectedJob_PropertyChanged;

            selectedJob = value;

            if (selectedJob is not null)
                selectedJob.PropertyChanged += SelectedJob_PropertyChanged;

            OnPropertyChanged();
            RefreshSelectedJobDetails();
            RefreshThanosReport();
        }
    }

    public MigrationLogEntry? SelectedLogEntry
    {
        get => selectedLogEntry;
        set => SetField(ref selectedLogEntry, value);
    }

    public string SelectedJobDetails
    {
        get => selectedJobDetails;
        private set => SetField(ref selectedJobDetails, value);
    }

    public string OutputDirectory
    {
        get => outputDirectory;
        set
        {
            if (SetField(ref outputDirectory, value))
            {
                RefreshJobOutputPaths();
                UpdateStatus($"Output directory set to {outputDirectory}.");
            }
        }
    }

    public string TextureManifestDirectory
    {
        get => textureManifestDirectory;
        set
        {
            if (SetField(ref textureManifestDirectory, value))
                UpdateStatus($"Texture manifest folder set to {textureManifestDirectory}.");
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetField(ref progressText, value);
    }

    public double OverallProgress
    {
        get => overallProgress;
        private set => SetField(ref overallProgress, value);
    }

    public string ResultsSummaryText
    {
        get => resultsSummaryText;
        private set => SetField(ref resultsSummaryText, value);
    }

    public string LogFilter
    {
        get => logFilter;
        set => SetField(ref logFilter, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    public bool IsThanosBusy
    {
        get => isThanosBusy;
        private set => SetField(ref isThanosBusy, value);
    }

    public MigrationMode CurrentMode
    {
        get => currentMode;
        set
        {
            if (SetField(ref currentMode, value))
            {
                OnPropertyChanged(nameof(IsThanosMode));
                OnPropertyChanged(nameof(CurrentModeIndex));
                RefreshSelectedJobDetails();
            }
        }
    }

    public int CurrentModeIndex
    {
        get => (int)CurrentMode;
        set
        {
            if (value < 0)
                value = 0;
            if (value > 1)
                value = 1;
            CurrentMode = (MigrationMode)value;
        }
    }

    public bool IsThanosMode => CurrentMode == MigrationMode.ThanosRaid;

    public ThanosMigrationReport? ThanosReport
    {
        get => thanosReport;
        private set => SetField(ref thanosReport, value);
    }

    public ObservableCollection<ThanosMigrationStep> ThanosSteps { get; } = [];

    public UpkMigrationViewModel(
        UpkMigrationService service,
        ThanosStructuralMigrationService thanosStructuralService,
        ThanosTextureMigrationService thanosTextureService,
        TfcManifestService tfcManifestService,
        DispatcherQueue? dispatcherQueue = null)
    {
        this.dispatcherQueue = dispatcherQueue;
        this.service = service;
        this.thanosStructuralService = thanosStructuralService;
        this.thanosTextureService = thanosTextureService;
        this.tfcManifestService = tfcManifestService;
        prototypeMerger = new ThanosPrototypeMergerViewModel(
            new ThanosPrototypeDiscoveryService(),
            new ThanosPrototypeMergePlanner(),
            new ThanosPrototypeMergerService());
        service.LogEntryAdded += Service_LogEntryAdded;
        service.ProgressChanged += Service_ProgressChanged;
        service.StatusChanged += Service_StatusChanged;
        service.JobUpdated += Service_JobUpdated;

        SelectUpksCommand = new AsyncRelayCommand(SelectUpksAsync);
        StartMigrationCommand = new AsyncRelayCommand(StartMigrationAsync);
        BrowseOutputDirectoryCommand = new AsyncRelayCommand(BrowseOutputDirectoryAsync);
        BrowseTextureManifestDirectoryCommand = new AsyncRelayCommand(BrowseTextureManifestDirectoryAsync);
        AnalyzeThanosCommand = new AsyncRelayCommand(AnalyzeThanosAsync);
        MigrateThanosCommand = new AsyncRelayCommand(MigrateThanosAsync);
        UpdateTextureManifestCommand = new AsyncRelayCommand(UpdateTextureManifestAsync);

        UpdateStatus("Ready.");
    }

    public async Task SelectUpksAsync()
    {
        IReadOnlyList<string> paths = SelectUpksRequestedAsync is null ? [] : await SelectUpksRequestedAsync();
        if (paths.Count == 0)
        {
            UpdateStatus("No UPKs were selected.");
            return;
        }

        Jobs.Clear();
        foreach (string path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            MigrationJob job = new()
            {
                SourceUpkPath = path,
                OutputUpkPath = BuildOutputPath(path),
                Status = MigrationJobStatus.Pending,
                CurrentStep = "Pending"
            };
            Jobs.Add(job);
        }

        RefreshJobOutputPaths();
        UpdateStatus($"{Jobs.Count} UPK file(s) queued for migration.");
        RefreshSelectedJobDetails();
        RefreshSummary();
    }

    public async Task StartMigrationAsync()
    {
        if (IsBusy)
            return;

        if (Jobs.Count == 0)
        {
            UpdateStatus("Select one or more 1.48 UPKs before starting migration.");
            return;
        }

        if (!TryEnsureOutputDirectory(out string? validatedOutputDirectory) || string.IsNullOrWhiteSpace(validatedOutputDirectory))
        {
            UpdateStatus("Choose a valid output directory before starting migration.");
            return;
        }

        IsBusy = true;
        try
        {
            foreach (MigrationJob job in Jobs)
                job.IsThanosRaid = IsThanosMode;

            ProgressText = "Starting migration pipeline...";
            try
            {
                await service.RunMigrationAsync(Jobs, validatedOutputDirectory, TextureManifestDirectory);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Migration failed: {ex}");
                ResultsSummaryText = ex.ToString();
            }
            StatusText = service.StatusText;
            ProgressText = service.ProgressText;
            OverallProgress = service.OverallProgress;
            ResultsSummaryText = string.IsNullOrWhiteSpace(ResultsSummaryText) ? service.LastRunSummaryText : ResultsSummaryText;
            RefreshSelectedJobDetails();
        }
        finally
        {
            IsBusy = false;
            RefreshSummary();
        }
    }

    public async Task BrowseOutputDirectoryAsync()
    {
        if (BrowseOutputDirectoryRequestedAsync is null)
            return;

        string? folder = await BrowseOutputDirectoryRequestedAsync();
        if (!string.IsNullOrWhiteSpace(folder))
            OutputDirectory = folder;
    }

    public async Task BrowseTextureManifestDirectoryAsync()
    {
        if (BrowseTextureManifestDirectoryRequestedAsync is null)
            return;

        string? folder = await BrowseTextureManifestDirectoryRequestedAsync();
        if (!string.IsNullOrWhiteSpace(folder))
            TextureManifestDirectory = folder;
    }

    public async Task AnalyzeThanosAsync()
    {
        if (IsBusy || IsThanosBusy)
            return;

        MigrationJob[] jobsToAnalyze = Jobs.Where(job => !string.IsNullOrWhiteSpace(job.SourceUpkPath)).ToArray();
        if (jobsToAnalyze.Length == 0)
        {
            UpdateStatus("Select a UPK before analyzing Thanos Raid mode.");
            return;
        }

        IsThanosBusy = true;
        try
        {
            CurrentMode = MigrationMode.ThanosRaid;
            PostToUi(() =>
            {
                ThanosSteps.Clear();
                ThanosReport = null;
                ThanosSteps.Add(new ThanosMigrationStep
                {
                    Name = "Structural Analysis",
                    Description = "Inspect header, tables, and export layout.",
                    Status = ThanosMigrationStepStatus.Pending
                });
                ThanosSteps.Add(new ThanosMigrationStep
                {
                    Name = "Texture Analysis",
                    Description = "Inspect Texture2D exports and streaming references.",
                    Status = ThanosMigrationStepStatus.Pending
                });
            });

            ThanosMigrationReport combinedReport = new()
            {
                FilePath = "Batch Thanos Analysis"
            };
            List<ThanosMigrationReport?> batchReports = [];
            for (int i = 0; i < jobsToAnalyze.Length; i++)
            {
                MigrationJob job = jobsToAnalyze[i];
                await InvokeOnUiAsync(() => SelectedJob = job).ConfigureAwait(false);
                ThanosMigrationReport? report = await AnalyzeThanosJobAsync(job, i + 1, jobsToAnalyze.Length).ConfigureAwait(false);
                batchReports.Add(report);
                if (report is not null)
                {
                    job.ThanosReport = report;
                    job.AnalyzeMeshCount = report.SkeletalMeshCount + report.StaticMeshCount;
                    job.AnalyzeTextureCount = report.TextureCount;
                    job.AnalyzeAnimationCount = report.AnimationCount;
                    job.AnalyzeMaterialCount = report.MaterialCount;
                    combinedReport.Findings.AddRange(report.Findings);
                    combinedReport.SkeletalMeshCount += report.SkeletalMeshCount;
                    combinedReport.StaticMeshCount += report.StaticMeshCount;
                    combinedReport.TextureCount += report.TextureCount;
                    combinedReport.AnimationCount += report.AnimationCount;
                    combinedReport.MaterialCount += report.MaterialCount;
                }
            }

            PostToUi(() =>
            {
                ThanosReport = batchReports.LastOrDefault(report => report is not null) ?? combinedReport;

                if (ThanosSteps.Count >= 2)
                {
                    ThanosSteps[0] = new ThanosMigrationStep
                    {
                        Name = "Structural Analysis",
                        Description = "Inspect header, tables, and export layout.",
                        Status = ThanosMigrationStepStatus.Done,
                        Reason = $"{combinedReport.Findings.Count} finding(s)"
                    };
                    ThanosSteps[1] = new ThanosMigrationStep
                    {
                        Name = "Texture Analysis",
                        Description = "Inspect Texture2D exports and streaming references.",
                        Status = ThanosMigrationStepStatus.Done,
                        Reason = $"{combinedReport.Findings.Count} finding(s)"
                    };
                }
            });

            UpdateStatus("Thanos Raid analysis complete.");
        }
        catch (Exception ex)
        {
            ThanosSteps.Clear();
            ThanosSteps.Add(new ThanosMigrationStep
            {
                Name = "Thanos Analysis",
                Description = "Analyze Thanos Raid package state.",
                Status = ThanosMigrationStepStatus.Failed,
                Reason = ex.Message,
                Exception = ex
            });

            UpdateStatus($"Thanos analysis failed: {ex.Message}");
        }
        finally
        {
            IsThanosBusy = false;
        }
    }

    public async Task MigrateThanosAsync()
    {
        CurrentMode = MigrationMode.ThanosRaid;
        foreach (MigrationJob job in Jobs)
            job.IsThanosRaid = true;

        await AnalyzeThanosAsync();
        await StartMigrationAsync();

        if (!string.IsNullOrWhiteSpace(OutputDirectory))
        {
            string manifestPath = Path.Combine(Path.GetFullPath(OutputDirectory), TextureManifest.ManifestName);
            if (File.Exists(manifestPath))
            {
                int manifestCount = tfcManifestService.LoadManifest(manifestPath).Count;
                UpdateStatus($"Thanos TFC manifest loaded: {manifestCount} entry(s).");
            }
        }
    }

    public Task UpdateTextureManifestAsync()
    {
        if (IsBusy || IsThanosBusy)
            return Task.CompletedTask;

        if (!IsThanosMode)
        {
            UpdateStatus("Switch to Thanos Raid mode before updating the texture manifest.");
            return Task.CompletedTask;
        }

        if (Jobs.Count == 0)
        {
            UpdateStatus("Load one or more Thanos UPKs before updating the texture manifest.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            UpdateStatus("Set the migration output directory first.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(TextureManifestDirectory))
        {
            UpdateStatus("Select the 1.52 client folder that contains TextureFileCacheManifest.bin.");
            return Task.CompletedTask;
        }

        string sourceManifestPath = Path.Combine(Path.GetFullPath(OutputDirectory), TextureManifest.ManifestName);
        if (!File.Exists(sourceManifestPath))
        {
            UpdateStatus("Run Batch Migrate first so the migrated texture manifest exists in the output folder.");
            return Task.CompletedTask;
        }

        string targetManifestPath = Path.Combine(Path.GetFullPath(TextureManifestDirectory), TextureManifest.ManifestName);
        if (!File.Exists(targetManifestPath))
        {
            UpdateStatus("Select the 1.52 client folder that already contains TextureFileCacheManifest.bin.");
            return Task.CompletedTask;
        }

        try
        {
            List<ThanosTfcEntry> sourceEntries = tfcManifestService.LoadManifest(sourceManifestPath);
            if (sourceEntries.Count == 0)
            {
                UpdateStatus("The migrated manifest did not contain any texture entries to inject.");
                return Task.CompletedTask;
            }

            List<ThanosTfcEntry> existingEntries = tfcManifestService.LoadManifest(targetManifestPath);
            List<ThanosTfcEntry> mergedEntries = existingEntries.Count > 0
                ? tfcManifestService.MergeEntries(existingEntries, sourceEntries)
                : sourceEntries;

            string backupManifestPath = Path.Combine(
                Path.GetDirectoryName(targetManifestPath) ?? TextureManifestDirectory!,
                $"TextureFileCacheManifest_{DateTime.Now:yyyyMMdd_HHmmss}.bin.bak");
            File.Copy(targetManifestPath, backupManifestPath, overwrite: true);

            tfcManifestService.SaveManifest(targetManifestPath, mergedEntries);
            UpdateStatus($"Texture manifest updated: {targetManifestPath} ({mergedEntries.Count:N0} entry(s)). Backup: {backupManifestPath}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Texture manifest update failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task<ThanosMigrationReport?> AnalyzeThanosJobAsync(MigrationJob job, int jobIndex, int totalJobs)
    {
        job.IsThanosRaid = true;
        await InvokeOnUiAsync(() =>
        {
            job.AnalyzeProgress = 0;
            job.CurrentStep = "Analyzing...";
        }).ConfigureAwait(false);

        try
        {
            await InvokeOnUiAsync(() =>
            {
                job.Status = MigrationJobStatus.Running;
                job.AnalyzeProgress = 0;
                job.CurrentStep = "Analyzing Thanos package...";
            }).ConfigureAwait(false);
            ThanosMigrationReport structuralReport = await thanosStructuralService.Analyze(
                job.SourceUpkPath,
                (progress, message) => PostToUi(() =>
                {
                    job.AnalyzeProgress = progress * 0.5;
                    job.CurrentStep = message;
                    ProgressText = $"Analyzing {jobIndex} of {totalJobs} UPK file(s)...";
                    StatusText = message;
                })).ConfigureAwait(false);

            IReadOnlyList<ThanosMigrationFinding> textureFindings = await thanosTextureService.AnalyzeTextures(
                job.SourceUpkPath,
                (progress, message) => PostToUi(() =>
                {
                    job.AnalyzeProgress = 50 + (progress * 0.5);
                    job.CurrentStep = message;
                    ProgressText = $"Analyzing {jobIndex} of {totalJobs} UPK file(s)...";
                    StatusText = message;
                })).ConfigureAwait(false);

            structuralReport.Findings.AddRange(textureFindings);
            structuralReport.FilePath = job.SourceUpkPath;
            await InvokeOnUiAsync(() =>
            {
                job.AnalyzeMeshCount = structuralReport.SkeletalMeshCount + structuralReport.StaticMeshCount;
                job.AnalyzeTextureCount = structuralReport.TextureCount;
                job.AnalyzeAnimationCount = structuralReport.AnimationCount;
                job.AnalyzeMaterialCount = structuralReport.MaterialCount;
                job.ThanosReport = structuralReport;
                job.Status = MigrationJobStatus.Completed;
                job.AnalyzeProgress = 100;
                job.CurrentStep = "Thanos analysis complete.";
            }).ConfigureAwait(false);

            return structuralReport;
        }
        catch (Exception ex)
        {
            ThanosMigrationReport failedReport = new()
            {
                FilePath = job.SourceUpkPath
            };
            await InvokeOnUiAsync(() =>
            {
                job.Status = MigrationJobStatus.Failed;
                job.ThanosReport = failedReport;
                job.AnalyzeMeshCount = 0;
                job.AnalyzeTextureCount = 0;
                job.AnalyzeAnimationCount = 0;
                job.AnalyzeMaterialCount = 0;
                job.AnalyzeProgress = 100;
                job.CurrentStep = $"Thanos analysis failed: {ex.Message}";
            }).ConfigureAwait(false);
            return failedReport;
        }
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    private void Service_LogEntryAdded(MigrationLogEntry entry)
    {
        PostToUi(() =>
        {
            LogEntries.Add(entry);
            ResultsSummaryText = service.LastRunSummaryText;
        });
    }

    private void Service_ProgressChanged(double progress, string message)
    {
        PostToUi(() =>
        {
            OverallProgress = progress;
            ProgressText = message;
            StatusText = message;
        });
    }

    private void Service_StatusChanged(string message)
    {
        PostToUi(() => StatusText = message);
    }

    private void Service_JobUpdated(MigrationJob job)
    {
        PostToUi(() =>
        {
            if (ReferenceEquals(SelectedJob, job))
                RefreshSelectedJobDetails();

            RefreshSummary();
        });
    }

    private void SelectedJob_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshSelectedJobDetails();
        RefreshThanosReport();
        RefreshSummary();
    }

    private void RefreshSelectedJobDetails()
    {
        SelectedJobDetails = SelectedJob?.DetailsText ?? "No job selected.";
    }

    private void RefreshThanosReport()
    {
        ThanosReport = SelectedJob?.ThanosReport;
    }

    private void RefreshSummary()
    {
        ResultsSummaryText = service.LastRunSummaryText;
    }

    private void RefreshJobOutputPaths()
    {
        foreach (MigrationJob job in Jobs)
            job.OutputUpkPath = BuildOutputPath(job.SourceUpkPath);
    }

    private bool TryEnsureOutputDirectory(out string? validatedOutputDirectory)
    {
        validatedOutputDirectory = null;
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            return false;

        try
        {
            validatedOutputDirectory = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(validatedOutputDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildOutputPath(string sourcePath)
    {
        if (!TryEnsureOutputDirectory(out string? validatedOutputDirectory) || string.IsNullOrWhiteSpace(validatedOutputDirectory))
            return string.Empty;

        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(validatedOutputDirectory, $"{fileName}_152.upk");
    }

    private void UpdateStatus(string message)
    {
        StatusText = message;
        service.StatusText = message;
    }

    private void PostToUi(Action action)
    {
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcherQueue.TryEnqueue(() => action());
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

    private sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task>? executeAsync;
        private readonly Func<object?, Task>? execute;

        public AsyncRelayCommand(Func<Task> execute)
        {
            executeAsync = execute;
        }

        public AsyncRelayCommand(Func<object?, Task> execute)
        {
            this.execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter)
        {
            try
            {
                if (executeAsync is not null)
                {
                    await executeAsync();
                    return;
                }

                if (execute is not null)
                    await execute(parameter);
            }
            catch (Exception)
            {
            }
        }
    }
}

