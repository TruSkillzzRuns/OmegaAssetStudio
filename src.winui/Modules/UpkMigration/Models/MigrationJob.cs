using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using OmegaAssetStudio.ThanosMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public enum MigrationJobStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed class MigrationJob : INotifyPropertyChanged
{
    private string sourceUpkPath = string.Empty;
    private string outputUpkPath = string.Empty;
    private MigrationJobStatus status = MigrationJobStatus.Pending;
    private string currentStep = "Pending";
    private int meshCount;
    private int textureCount;
    private int animationCount;
    private int materialCount;
    private int analyzeMeshCount;
    private int analyzeTextureCount;
    private int analyzeAnimationCount;
    private int analyzeMaterialCount;
    private int warningCount;
    private int errorCount;
    private double analyzeProgress;
    private double migrateProgress;
    private string cacheKey = string.Empty;
    private string sourceFingerprint = string.Empty;
    private string schemaVersion = string.Empty;
    private string analyzerVersion = string.Empty;
    private bool isThanosRaid;
    private MigrationResult? result;
    private ThanosMigrationReport? thanosReport;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetField(ref sourceUpkPath, value);
    }

    public string SourceFileName => string.IsNullOrWhiteSpace(SourceUpkPath) ? string.Empty : Path.GetFileName(SourceUpkPath);

    public string OutputUpkPath
    {
        get => outputUpkPath;
        set => SetField(ref outputUpkPath, value);
    }

    public MigrationJobStatus Status
    {
        get => status;
        set
        {
            if (SetField(ref status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status.ToString();

    public string CurrentStep
    {
        get => currentStep;
        set => SetField(ref currentStep, value);
    }

    public int MeshCount
    {
        get => meshCount;
        set => SetField(ref meshCount, value);
    }

    public int TextureCount
    {
        get => textureCount;
        set => SetField(ref textureCount, value);
    }

    public int AnimationCount
    {
        get => animationCount;
        set => SetField(ref animationCount, value);
    }

    public int MaterialCount
    {
        get => materialCount;
        set => SetField(ref materialCount, value);
    }

    public int AnalyzeMeshCount
    {
        get => analyzeMeshCount;
        set => SetField(ref analyzeMeshCount, value);
    }

    public int AnalyzeTextureCount
    {
        get => analyzeTextureCount;
        set => SetField(ref analyzeTextureCount, value);
    }

    public int AnalyzeAnimationCount
    {
        get => analyzeAnimationCount;
        set => SetField(ref analyzeAnimationCount, value);
    }

    public int AnalyzeMaterialCount
    {
        get => analyzeMaterialCount;
        set => SetField(ref analyzeMaterialCount, value);
    }

    public int WarningCount
    {
        get => warningCount;
        set => SetField(ref warningCount, value);
    }

    public int ErrorCount
    {
        get => errorCount;
        set => SetField(ref errorCount, value);
    }

    public double AnalyzeProgress
    {
        get => analyzeProgress;
        set => SetField(ref analyzeProgress, value);
    }

    public double MigrateProgress
    {
        get => migrateProgress;
        set => SetField(ref migrateProgress, value);
    }

    public string CacheKey
    {
        get => cacheKey;
        set => SetField(ref cacheKey, value);
    }

    public string SourceFingerprint
    {
        get => sourceFingerprint;
        set => SetField(ref sourceFingerprint, value);
    }

    public string SchemaVersion
    {
        get => schemaVersion;
        set => SetField(ref schemaVersion, value);
    }

    public string AnalyzerVersion
    {
        get => analyzerVersion;
        set => SetField(ref analyzerVersion, value);
    }

    public bool IsThanosRaid
    {
        get => isThanosRaid;
        set
        {
            if (SetField(ref isThanosRaid, value))
                OnPropertyChanged(nameof(ModeText));
        }
    }

    public MigrationResult? Result
    {
        get => result;
        set => SetField(ref result, value);
    }

    public ThanosMigrationReport? ThanosReport
    {
        get => thanosReport;
        set => SetField(ref thanosReport, value);
    }

    public string ModeText => IsThanosRaid ? "Thanos Raid" : "Standard";

    public string AnalyzeProgressText => $"Analyze {AnalyzeProgress:0}%";

    public string MigrateProgressText => $"Migrate {MigrateProgress:0}%";

    public string DetailsText =>
        $"{SourceFileName}{System.Environment.NewLine}" +
        $"Input: {SourceUpkPath}{System.Environment.NewLine}" +
        $"Output: {OutputUpkPath}{System.Environment.NewLine}" +
        $"Mode: {ModeText}{System.Environment.NewLine}" +
        $"Step: {CurrentStep}{System.Environment.NewLine}" +
        $"Schema: {SchemaVersion}  Analyzer: {AnalyzerVersion}{System.Environment.NewLine}" +
        $"Analyze: {AnalyzeProgress:0}%  Migrate: {MigrateProgress:0}%{System.Environment.NewLine}" +
        $"Analyze Assets: Meshes {AnalyzeMeshCount}  Textures {AnalyzeTextureCount}  Animations {AnalyzeAnimationCount}  Materials {AnalyzeMaterialCount}{System.Environment.NewLine}" +
        $"Meshes: {MeshCount}  Textures: {TextureCount}  Animations: {AnimationCount}  Materials: {MaterialCount}{System.Environment.NewLine}" +
        $"Warnings: {WarningCount}  Errors: {ErrorCount}";

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(DetailsText));
        if (string.Equals(propertyName, nameof(AnalyzeProgress), System.StringComparison.Ordinal))
            OnPropertyChanged(nameof(AnalyzeProgressText));
        if (string.Equals(propertyName, nameof(MigrateProgress), System.StringComparison.Ordinal))
            OnPropertyChanged(nameof(MigrateProgressText));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

