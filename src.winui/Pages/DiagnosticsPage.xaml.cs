using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class DiagnosticsPage : Page
{
    private bool diagnosticsLoaded;

    public DiagnosticsPage()
    {
        InitializeComponent();
        Loaded += DiagnosticsPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshRuntimeState();
        QueueDiagnosticsRefresh();
    }

    private void DiagnosticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (diagnosticsLoaded)
            return;

        diagnosticsLoaded = true;
        RefreshRuntimeState();
        QueueDiagnosticsRefresh();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRuntimeState();
        QueueDiagnosticsRefresh();
    }

    private async void CopySnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        package.SetText(SnapshotBox.Text ?? string.Empty);
        Clipboard.SetContent(package);
        await Task.CompletedTask;
    }

    private async void CopyLogsButton_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        package.SetText(LogsBox.Text ?? string.Empty);
        Clipboard.SetContent(package);
        await Task.CompletedTask;
    }

    private async void RunMaterialProbeButton_Click(object sender, RoutedEventArgs e)
    {
        ProbeBox.Text = "Running material probe...";

        object? page = App.MainWindow?.CurrentPage;
        if (page is null || string.Equals(page.GetType().Name, "DiagnosticsPage", StringComparison.Ordinal))
            page = App.MainWindow?.LastWorkspacePage;

        if (page is null)
        {
            ProbeBox.Text = "No workspace page is available.";
            return;
        }

        MethodInfo? method = page.GetType().GetMethod("RunMaterialProbeAsync", BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
        {
            ProbeBox.Text = $"Current page: {page.GetType().Name}\nNo material probe is available.";
            return;
        }

        try
        {
            object? result = method.Invoke(page, null);
            if (result is Task<string> task)
            {
                ProbeBox.Text = await task.ConfigureAwait(true);
                return;
            }

            ProbeBox.Text = result as string ?? "Material probe did not return a report.";
        }
        catch (Exception ex)
        {
            ProbeBox.Text = $"Material probe failed while running the current Mesh workspace: {ex.Message}";
        }
    }

    private void RefreshRuntimeState()
    {
        RuntimeStateText.Text = BuildRuntimeStateText();
        if (string.IsNullOrWhiteSpace(ProbeBox.Text))
            ProbeBox.Text = "Run the probe against the current Mesh workspace to sweep camera states and log material behavior.";
        SnapshotBox.Text = "Loading snapshot...";
        LogsBox.Text = "Loading logs...";
    }

    private void QueueDiagnosticsRefresh()
    {
        _ = Task.Run(() =>
        {
            string snapshot = BuildSnapshotText();
            string logs = BuildLogsText();

            if (!DispatcherQueue.TryEnqueue(() =>
            {
                SnapshotBox.Text = snapshot;
                LogsBox.Text = logs;
            }))
            {
                SnapshotBox.Text = snapshot;
                LogsBox.Text = logs;
            }
        });
    }

    private static string BuildRuntimeStateText()
    {
        StringBuilder sb = new();
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine($"App Tag: {App.MainWindow?.CurrentTag ?? "(none)"}");
        sb.AppendLine($"Current Page: {App.MainWindow?.CurrentPage?.GetType().Name ?? "(none)"}");
        sb.AppendLine($"Last Workspace Page: {App.MainWindow?.LastWorkspacePage?.GetType().Name ?? "(none)"}");
        sb.AppendLine($"Diagnostics Log: {RuntimeLogPaths.DiagnosticsLogPath}");
        sb.AppendLine($"Crash Log: {RuntimeLogPaths.CrashLogPath}");
        sb.AppendLine($"Mesh Log: {RuntimeLogPaths.MeshErrorLogPath}");
        sb.AppendLine($"UPK Migration Log: {RuntimeLogPaths.UpkMigrationLogPath}");
        sb.AppendLine($"Objects Log: {RuntimeLogPaths.ObjectsLogPath}");
        sb.AppendLine($"Material Probe Log: {RuntimeLogPaths.MaterialProbeLogPath}");
        return sb.ToString();
    }

    private static string BuildSnapshotText()
    {
        object? page = App.MainWindow?.CurrentPage;
        if (page is null || string.Equals(page.GetType().Name, "DiagnosticsPage", StringComparison.Ordinal))
            page = App.MainWindow?.LastWorkspacePage;

        if (page is null)
            return "No page is currently loaded.";

        MethodInfo? method = page.GetType().GetMethod("BuildDiagnosticsReport", BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
            return $"Current page: {page.GetType().Name}\nNo diagnostics snapshot method is available.";

        object? result = method.Invoke(page, null);
        return result as string ?? $"Current page: {page.GetType().Name}\nDiagnostics snapshot is unavailable.";
    }

    private static string BuildLogsText()
    {
        StringBuilder sb = new();
        AppendLogTail(sb, "Diagnostics", RuntimeLogPaths.DiagnosticsLogPath, 120);
        AppendLogTail(sb, "Mesh", RuntimeLogPaths.MeshErrorLogPath, 80);
        AppendLogTail(sb, "UPK Migration", RuntimeLogPaths.UpkMigrationLogPath, 120);
        AppendLogTail(sb, "Crash", RuntimeLogPaths.CrashLogPath, 80);
        AppendLogTail(sb, "Objects", RuntimeLogPaths.ObjectsLogPath, 80);
        AppendLogTail(sb, "MaterialProbe", RuntimeLogPaths.MaterialProbeLogPath, 80);
        return sb.ToString();
    }

    private static void AppendLogTail(StringBuilder sb, string label, string path, int maxLines)
    {
        sb.AppendLine($"[{label}] {path}");

        if (!File.Exists(path))
        {
            sb.AppendLine("(log file not found)");
            sb.AppendLine();
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(path);
            int start = Math.Max(0, lines.Length - maxLines);
            for (int i = start; i < lines.Length; i++)
            {
                sb.AppendLine(lines[i]);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(failed to read log tail: {ex.Message})");
        }

        sb.AppendLine();
    }
}

