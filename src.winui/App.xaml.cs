using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using OmegaAssetStudio.TextureManager;
using OmegaAssetStudio.WinUI.Services;

namespace OmegaAssetStudio.WinUI;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    private static readonly string CrashLogPath = RuntimeLogPaths.CrashLogPath;
    private static readonly string DiagnosticsLogPath = RuntimeLogPaths.DiagnosticsLogPath;
    private static readonly string MaterialProbeLogPath = RuntimeLogPaths.MaterialProbeLogPath;

    public App()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            ThemeService.Initialize();
            TextureManifest.Initialize();
            TextureFileCache.Initialize();
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnLaunched exception", ex.ToString());
            throw;
        }
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject?.ToString() ?? "(null)");
    }

    private static void CurrentDomain_FirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        Exception exception = e.Exception;
        if (exception is null)
            return;

        string message = exception.ToString();
        if (!message.Contains("OmegaAssetStudio", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("OmegaAssetStudio", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("UpkManager", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        WriteDiagnosticsLog("FirstChanceException", message);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception.ToString());
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("Microsoft.UI.Xaml.UnhandledException", e.Exception.ToString());
    }

    private static void WriteCrashLog(string source, string details)
    {
        try
        {
            DateTime now = DateTime.Now;
            StringBuilder builder = new();
            builder.AppendLine("Omega Asset Studio crash log");
            builder.AppendLine($"Time: {now:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine();
            builder.AppendLine(details);
            builder.AppendLine(new string('-', 80));
            File.AppendAllText(CrashLogPath, builder.ToString());
            File.WriteAllText(RuntimeLogPaths.GetCrashSnapshotPath(now), builder.ToString());
        }
        catch
        {
        }
    }

    public static void WriteDiagnosticsLog(string source, string details)
    {
        try
        {
            StringBuilder builder = new();
            builder.AppendLine("Omega Asset Studio diagnostics");
            builder.AppendLine($"Time: {DateTime.Now:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine();
            builder.AppendLine(details);
            builder.AppendLine(new string('-', 80));
            File.AppendAllText(DiagnosticsLogPath, builder.ToString());
        }
        catch
        {
        }
    }

    public static void WriteMaterialProbeLog(string source, string details)
    {
        try
        {
            StringBuilder builder = new();
            builder.AppendLine("Omega Asset Studio material probe");
            builder.AppendLine($"Time: {DateTime.Now:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine();
            builder.AppendLine(details);
            builder.AppendLine(new string('-', 80));
            File.AppendAllText(MaterialProbeLogPath, builder.ToString());
        }
        catch
        {
        }
    }
}

