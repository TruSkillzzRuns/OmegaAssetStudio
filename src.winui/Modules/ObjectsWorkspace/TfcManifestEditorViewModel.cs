using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using OmegaAssetStudio.Services;
using OmegaAssetStudio.TfcManifest;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace;

public sealed class TfcManifestEditorViewModel : INotifyPropertyChanged
{
    private readonly TfcManifestEditorService service = new();
    private TfcManifestDocument? document;
    private TfcManifestEntry? selectedEntry;
    private string? manifestPath;
    private bool isBusy;
    private bool packageNameSortAscending = true;
    private bool validationSortAscending = true;
    private string statusText = "Open a TextureFileCacheManifest.bin to begin.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TfcManifestEntry> Entries { get; } = [];

    public ObservableCollection<string> ValidationResults { get; } = [];

    public string? ManifestPath
    {
        get => manifestPath;
        set => SetField(ref manifestPath, value);
    }

    public TfcManifestDocument? Document
    {
        get => document;
        private set => SetField(ref document, value);
    }

    public TfcManifestEntry? SelectedEntry
    {
        get => selectedEntry;
        set => SetField(ref selectedEntry, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string PackageNameSortLabel => packageNameSortAscending ? "PackageName â–²" : "PackageName â–¼";

    public string ValidationSortLabel => validationSortAscending ? "Validation â–²" : "Validation â–¼";

    public ICommand OpenManifestCommand { get; }

    public ICommand SaveManifestCommand { get; }

    public ICommand AddEntryCommand { get; }

    public ICommand RemoveEntryCommand { get; }

    public ICommand ApplyEntryChangesCommand { get; }

    public ICommand InjectEntriesCommand { get; }

    public TfcManifestEditorViewModel()
    {
        OpenManifestCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        SaveManifestCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        AddEntryCommand = new RelayCommand(AddEntry);
        RemoveEntryCommand = new RelayCommand(RemoveSelectedEntry, () => SelectedEntry is not null);
        ApplyEntryChangesCommand = new RelayCommand(ApplySelectedEntryChanges, () => SelectedEntry is not null);
        InjectEntriesCommand = new AsyncRelayCommand(() => Task.CompletedTask);
    }

    public Task OpenManifestAsync(string path)
    {
        if (IsBusy)
            return Task.CompletedTask;

        IsBusy = true;
        try
        {
            string resolvedPath = ResolveManifestPath(path);
            Document = service.Load(resolvedPath);
            ManifestPath = resolvedPath;
            Entries.Clear();
            foreach (TfcManifestEntry entry in Document.Entries)
                Entries.Add(entry.Clone());

            RefreshValidation();
            StatusText = $"Loaded {Entries.Count:N0} manifest entr{(Entries.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load manifest: {ex.Message}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    public Task SaveManifestAsync(string? path = null)
    {
        if (IsBusy)
            return Task.CompletedTask;

        if (Document is null)
        {
            StatusText = "Open a manifest before saving.";
            return Task.CompletedTask;
        }

        IsBusy = true;
        try
        {
            SyncEntriesToDocument();
            string outputPath = ResolveManifestPath(path ?? ManifestPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                StatusText = "Select a manifest path before saving.";
                return Task.CompletedTask;
            }

            if (File.Exists(outputPath))
            {
                string backupPath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? ".",
                    $"TextureFileCacheManifest_{DateTime.Now:yyyyMMdd_HHmmss}.bin.bak");
                File.Copy(outputPath, backupPath, overwrite: true);
            }

            service.Save(outputPath, Document);
            ManifestPath = outputPath;
            StatusText = $"Manifest saved: {outputPath}";
            RefreshValidation();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save manifest: {ex.Message}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    public void AddEntry()
    {
        TfcManifestEntry entry = new()
        {
            TextureName = "new_texture",
            TfcFileName = "new_texture.tfc"
        };

        Entries.Add(entry);
        SelectedEntry = entry;
        SyncEntriesToDocument();
        RefreshValidation();
        StatusText = "Entry added.";
    }

    public void RemoveSelectedEntry()
    {
        if (SelectedEntry is null)
            return;

        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.LastOrDefault();
        SyncEntriesToDocument();
        RefreshValidation();
        StatusText = "Entry removed.";
    }

    public void ApplySelectedEntryChanges()
    {
        if (SelectedEntry is null)
            return;

        SelectedEntry.Normalize();
        SyncEntriesToDocument();
        RefreshValidation();
        StatusText = "Entry changes applied.";
    }

    public void SortEntriesByPackageName()
    {
        packageNameSortAscending = !packageNameSortAscending;
        SortEntriesByPackageNameInternal();
        OnPropertyChanged(nameof(PackageNameSortLabel));
        StatusText = $"Entries sorted by package name ({(packageNameSortAscending ? "A-Z" : "Z-A")}).";
    }

    public void SortValidationResults()
    {
        validationSortAscending = !validationSortAscending;
        SortValidationResultsInternal();
        OnPropertyChanged(nameof(ValidationSortLabel));
        StatusText = $"Validation results sorted ({(validationSortAscending ? "A-Z" : "Z-A")}).";
    }

    public Task InjectEntriesAsync(string sourcePath)
    {
        if (IsBusy)
            return Task.CompletedTask;

        IsBusy = true;
        try
        {
            List<TfcManifestEntry> importedEntries = [];
            if (File.Exists(sourcePath))
            {
                if (string.Equals(Path.GetExtension(sourcePath), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    string json = File.ReadAllText(sourcePath);
                    importedEntries = JsonSerializer.Deserialize<List<TfcManifestEntry>>(json) ?? [];
                }
                else if (string.Equals(Path.GetFileName(sourcePath), "TextureFileCacheManifest.bin", StringComparison.OrdinalIgnoreCase))
                {
                    TfcManifestDocument imported = service.Load(sourcePath);
                    importedEntries = imported.Entries.Select(entry => entry.Clone()).ToList();
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                string candidate = Path.Combine(sourcePath, "TextureFileCacheManifest.bin");
                if (File.Exists(candidate))
                {
                    TfcManifestDocument imported = service.Load(candidate);
                    importedEntries = imported.Entries.Select(entry => entry.Clone()).ToList();
                }
            }

            if (importedEntries.Count == 0)
            {
                StatusText = "No entries were found to inject.";
                return Task.CompletedTask;
            }

            Document ??= new TfcManifestDocument();
            service.InjectEntries(Document, importedEntries);
            foreach (TfcManifestEntry entry in importedEntries)
                Entries.Add(entry);

            SyncEntriesToDocument();
            RefreshValidation();
            StatusText = $"Injected {importedEntries.Count:N0} entr{(importedEntries.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to inject entries: {ex.Message}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    public void SetManifestPath(string path)
    {
        ManifestPath = path;
        if (Document is not null)
            Document.SourceDirectory = GetSourceDirectory(path);
        RefreshValidation();
    }

    private static string ResolveManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (Directory.Exists(path))
            return Path.Combine(path, "TextureFileCacheManifest.bin");

        if (File.Exists(path))
            return path;

        if (string.Equals(Path.GetFileName(path), "TextureFileCacheManifest.bin", StringComparison.OrdinalIgnoreCase))
            return path;

        return path;
    }

    private static string? GetSourceDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Directory.Exists(path))
            return path;

        return Path.GetDirectoryName(path);
    }

    private void SyncEntriesToDocument()
    {
        if (Document is null)
            Document = new TfcManifestDocument();

        Document.Entries.Clear();
        foreach (TfcManifestEntry entry in Entries)
            Document.Entries.Add(entry.Clone());
    }

    private void SortEntriesByPackageNameInternal()
    {
        TfcManifestEntry? selected = SelectedEntry;
        IEnumerable<TfcManifestEntry> sorted = packageNameSortAscending
            ? Entries.OrderBy(static entry => entry.PackageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.TextureName, StringComparer.OrdinalIgnoreCase)
            : Entries.OrderByDescending(static entry => entry.PackageName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(static entry => entry.TextureName, StringComparer.OrdinalIgnoreCase);

        List<TfcManifestEntry> materialized = sorted.ToList();
        Entries.Clear();
        foreach (TfcManifestEntry entry in materialized)
            Entries.Add(entry);

        SelectedEntry = selected is null
            ? Entries.FirstOrDefault()
            : Entries.FirstOrDefault(entry => ReferenceEquals(entry, selected));
    }

    private void SortValidationResultsInternal()
    {
        IEnumerable<string> sorted = validationSortAscending
            ? ValidationResults.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            : ValidationResults.OrderByDescending(static value => value, StringComparer.OrdinalIgnoreCase);

        List<string> materialized = sorted.ToList();
        ValidationResults.Clear();
        foreach (string issue in materialized)
            ValidationResults.Add(issue);
    }

    private void RefreshValidation()
    {
        ValidationResults.Clear();
        if (Document is null)
        {
            ValidationResults.Add("Open a manifest to validate entries.");
            return;
        }

        Document.SourceDirectory = !string.IsNullOrWhiteSpace(ManifestPath)
            ? Path.GetDirectoryName(ManifestPath)
            : Document.SourceDirectory;

        foreach (string issue in service.Validate(Document))
            ValidationResults.Add(issue);

        if (ValidationResults.Count == 0)
            ValidationResults.Add("No validation issues found.");
    }

    private void OnSelectedEntryChanged()
    {
        (RemoveEntryCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyEntryChangesCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(SelectedEntry))
            OnSelectedEntryChanged();
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool>? canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => execute();

        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> execute;
        private bool isRunning;

        public AsyncRelayCommand(Func<Task> execute)
        {
            this.execute = execute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isRunning;

        public async void Execute(object? parameter)
        {
            if (isRunning)
                return;

            try
            {
                isRunning = true;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                await execute().ConfigureAwait(false);
            }
            finally
            {
                isRunning = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

