using System.Text.Json;
using OmegaAssetStudio.WinUI;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Configuration;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

internal static class UpkMigrationConfigStore
{
    private static readonly string StoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaAssetStudio", "OmegaAssetStudio.config.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static UpkMigrationConfig Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return CreateDefault();

            UpkMigrationConfig? config = JsonSerializer.Deserialize<UpkMigrationConfig>(File.ReadAllText(StoragePath), JsonOptions);
            return config ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static void Save(UpkMigrationConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch
        {
        }
    }

    private static UpkMigrationConfig CreateDefault()
    {
        return new UpkMigrationConfig
        {
            OutputRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_UpkMigration"),
            ResourcePrototypeOutputRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_UpkMigration_ResourceScanner"),
            BackportOutputRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_UpkMigration_Backport"),
            BackportTargetRoot = string.Empty,
            BackportRefreshPackageIndex = true,
            LogPath = RuntimeLogPaths.UpkMigrationResourceScannerLogPath,
            BackportLogPath = RuntimeLogPaths.UpkMigrationLogPath
        };
    }
}
