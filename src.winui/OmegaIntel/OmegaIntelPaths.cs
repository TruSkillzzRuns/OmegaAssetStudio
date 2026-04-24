using System;
using System.IO;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

internal static class OmegaIntelPaths
{
    private static readonly string RootDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaIntel");
    private static readonly string MigrationReadinessDirectoryName =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaIntel_MigrationReadiness");

    public static string LogsDirectory => GetDirectory("Logs");
    public static string ReportsDirectory => GetDirectory("Reports");
    public static string CacheDirectory => GetDirectory("Cache");
    public static string MigrationReadinessDirectory => GetRootDirectory(MigrationReadinessDirectoryName);

    public static string LatestScanCachePath => Path.Combine(CacheDirectory, "latest-scan.json");
    public static string LatestGraphCachePath => Path.Combine(CacheDirectory, "latest-graph.json");
    public static string LatestMigrationReadinessPath => Path.Combine(MigrationReadinessDirectory, "latest-migration-readiness.json");
    public static string AnalysisLogPath => Path.Combine(LogsDirectory, "omegaintel.log");

    public static string GetDirectory(string name)
    {
        string path = Path.Combine(RootDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetReportPath(string fileName) => Path.Combine(ReportsDirectory, fileName);

    public static string GetMigrationReadinessPath(string fileName) => Path.Combine(MigrationReadinessDirectory, fileName);

    public static string GetLogSnapshotPath(DateTime timestamp) =>
        Path.Combine(LogsDirectory, $"omegaintel_{timestamp:yyyyMMdd_HHmmss_fff}.txt");

    private static string GetRootDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

