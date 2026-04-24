using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Commands;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Interop;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Services;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.ViewModels;

public sealed class ObjectsWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly UpkReader reader = new();
    private readonly UpkRebuildService rebuildService = new();
    private readonly ObjectsRelayCommand applyHexChangesCommand;
    private UpkPackage? currentPackage;
    private UpkExportEntry? selectedExport;
    private string statusText = "Open a UPK to begin.";
    private string packageSummary = "No package loaded.";
    private string packagePath = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObjectsWorkspaceViewModel()
    {
        applyHexChangesCommand = new ObjectsRelayCommand(ApplyHexChanges, CanApplyHexChanges);
    }

    public ObservableCollection<UpkExportEntry> Exports { get; } = [];

    public ICommand ApplyHexChangesCommand => applyHexChangesCommand;

    public HexEditorViewModel HexEditor { get; } = new();

    public UpkPackage? CurrentPackage
    {
        get => currentPackage;
        private set
        {
            if (!SetField(ref currentPackage, value))
                return;

            applyHexChangesCommand.NotifyCanExecuteChanged();
        }
    }

    public UpkExportEntry? SelectedExport
    {
        get => selectedExport;
        set
        {
            if (!SetField(ref selectedExport, value))
                return;

            applyHexChangesCommand.NotifyCanExecuteChanged();
        }
    }

    public string PackagePath
    {
        get => packagePath;
        private set => SetField(ref packagePath, value);
    }

    public string PackageSummary
    {
        get => packageSummary;
        private set => SetField(ref packageSummary, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetField(ref statusText, value);
    }

    public async Task LoadUpkAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Select a UPK first.";
            return;
        }

        CurrentPackage = await Task.Run(() => reader.Read(path));
        PackagePath = path;
        PackageSummary = $"{Path.GetFileName(path)} | Names={CurrentPackage.Names.Count:N0} | Imports={CurrentPackage.Imports.Count:N0} | Exports={CurrentPackage.Exports.Count:N0}";
        StatusText = $"Loaded {Path.GetFileName(path)}.";

        Exports.Clear();
        foreach (UpkExportEntry export in CurrentPackage.Exports)
            Exports.Add(export);

        SelectedExport = Exports.FirstOrDefault();
        if (SelectedExport is not null)
            LoadExport(SelectedExport);
        else
            HexEditor.LoadBytes(Array.Empty<byte>(), "Hex Editor");
    }

    public void LoadExport(UpkExportEntry? export)
    {
        SelectedExport = export;
        if (export is null)
        {
            HexEditor.LoadBytes(Array.Empty<byte>(), "Hex Editor");
            StatusText = "No export selected.";
            return;
        }

        HexEditor.LoadBytes(export.RawData, export.ObjectName);
        StatusText = $"Loaded export {export.ObjectName}.";
    }

    public bool ApplyHexTextToSelectedExport(out string message)
    {
        message = "No export selected.";
        if (SelectedExport is null)
            return false;

        if (!HexEditor.TryCommitHexText(HexEditor.HexText, out byte[] bytes, out message))
            return false;

        SelectedExport.RawData = bytes;
        SelectedExport.SerialSize = bytes.Length;
        if (CurrentPackage is not null)
            rebuildService.ApplyHexEditToExport(CurrentPackage, SelectedExport, bytes);

        PackageSummary = CurrentPackage is null
            ? PackageSummary
            : $"{Path.GetFileName(CurrentPackage.OriginalPath)} | Names={CurrentPackage.Names.Count:N0} | Imports={CurrentPackage.Imports.Count:N0} | Exports={CurrentPackage.Exports.Count:N0}";

        StatusText = message;
        return true;
    }

    private bool CanApplyHexChanges() => CurrentPackage is not null && SelectedExport is not null;

    private void ApplyHexChanges()
    {
        ApplyHexTextToSelectedExport(out _);
    }

    public async Task SaveRebuiltAsync(string outputPath)
    {
        if (CurrentPackage is null)
        {
            StatusText = "Open a UPK before saving.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(HexEditor.HexText) && SelectedExport is not null)
            ApplyHexTextToSelectedExport(out _);

        rebuildService.RecalculateExportOffsets(CurrentPackage);
        rebuildService.RecalculateSummary(CurrentPackage);
        rebuildService.RebuildAndSavePackage(CurrentPackage, outputPath);
        PackagePath = outputPath;
        PackageSummary = $"{Path.GetFileName(outputPath)} | Names={CurrentPackage.Names.Count:N0} | Imports={CurrentPackage.Imports.Count:N0} | Exports={CurrentPackage.Exports.Count:N0}";
        StatusText = $"Saved rebuilt package: {outputPath}";
        await Task.CompletedTask;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
