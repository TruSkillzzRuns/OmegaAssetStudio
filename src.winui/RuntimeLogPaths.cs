using System;
using System.IO;

namespace OmegaAssetStudio.WinUI;

internal static class RuntimeLogPaths
{
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_RuntimeLogs");

    public static string CrashLogPath => GetLogPath("OmegaAssetStudio_WinUI_crash.log");
    public static string DiagnosticsLogPath => GetLogPath("OmegaAssetStudio_WinUI_diagnostics.log");
    public static string MeshErrorLogPath => GetLogPath("OmegaAssetStudio_WinUI_mesh_errors.log");
    public static string MflLogPath => GetLogPath("OmegaAssetStudio_WinUI_mfl.log");
    public static string UpkMigrationLogDirectory => GetLogDirectory("UpkMigration");
    public static string UpkMigrationLogPath => GetLogPath(Path.Combine("UpkMigration", "OmegaAssetStudio_WinUI_upk_migration.log"));
    public static string ObjectsLogPath => GetLogPath("OmegaAssetStudio_WinUI_objects.log");
    public static string MaterialProbeLogPath => GetLogPath("OmegaAssetStudio_WinUI_material_probe.log");
    public static string GetCrashSnapshotPath(DateTime timestamp) => GetLogPath($"OmegaAssetStudio_WinUI_crash_{timestamp:yyyyMMdd_HHmmss_fff}.txt");

    public static string GetLogPath(string fileName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GetFullLogPath(fileName)) ?? LogDirectory);
        return Path.Combine(LogDirectory, fileName);
    }

    public static string GetLogDirectory(string folderName)
    {
        string directory = Path.Combine(LogDirectory, folderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetFullLogPath(string fileName) => Path.Combine(LogDirectory, fileName);
}

