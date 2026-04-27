using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class UpkBackportPackageStatusRow : INotifyPropertyChanged
{
    private string packageName = string.Empty;
    private string status = string.Empty;
    private string sourcePath = string.Empty;
    private string outputPath = string.Empty;
    private string deployedPath = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PackageName
    {
        get => packageName;
        set => SetField(ref packageName, value);
    }

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    public string SourcePath
    {
        get => sourcePath;
        set => SetField(ref sourcePath, value);
    }

    public string OutputPath
    {
        get => outputPath;
        set => SetField(ref outputPath, value);
    }

    public string DeployedPath
    {
        get => deployedPath;
        set => SetField(ref deployedPath, value);
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
}
