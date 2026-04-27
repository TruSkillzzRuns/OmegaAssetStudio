using System.Text;
using System.Text.Json;
using UpkIndexGenerator;
using UpkManager.Contracts;
using UpkManager.Repository;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkDeploymentAssistantService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<UpkDeploymentReport> DeployAsync(
        string sourceUpkPath,
        string targetClientRoot,
        string deployFileName,
        string clientMapName,
        bool refreshPackageIndex,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(sourceUpkPath))
            throw new ArgumentException("A source UPK is required.", nameof(sourceUpkPath));

        if (!File.Exists(sourceUpkPath))
            throw new FileNotFoundException("Source UPK not found.", sourceUpkPath);

        if (string.IsNullOrWhiteSpace(targetClientRoot))
            throw new ArgumentException("A target client root is required.", nameof(targetClientRoot));

        if (string.IsNullOrWhiteSpace(deployFileName))
            throw new ArgumentException("A deploy file name is required.", nameof(deployFileName));

        string fullTargetClientRoot = Path.GetFullPath(targetClientRoot);
        Directory.CreateDirectory(fullTargetClientRoot);
        string destinationPath = Path.Combine(fullTargetClientRoot, deployFileName);

        UpkDeploymentReport report = new()
        {
            SourceUpkPath = Path.GetFullPath(sourceUpkPath),
            TargetClientRoot = fullTargetClientRoot,
            DeployFileName = deployFileName,
            DeployedPath = destinationPath,
            ClientMapName = clientMapName,
            RefreshPackageIndex = refreshPackageIndex
        };

        try
        {
            File.Copy(report.SourceUpkPath, destinationPath, overwrite: true);
            report.Notes.Add($"Copied {Path.GetFileName(report.SourceUpkPath)} to {destinationPath}.");
            log?.Invoke($"Deployed {Path.GetFileName(report.SourceUpkPath)}.");

            if (refreshPackageIndex)
            {
                string packageIndexPath = await RefreshPackageIndexAsync(fullTargetClientRoot, log).ConfigureAwait(false);
                report.PackageIndexPath = packageIndexPath;
                report.Notes.Add($"Package index refreshed: {packageIndexPath}");
            }
            else
            {
                report.Notes.Add("Package index refresh skipped.");
            }

            if (!string.IsNullOrWhiteSpace(clientMapName))
                report.Notes.Add($"ClientMap name: {clientMapName}");
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.Message);
            log?.Invoke($"Deployment failed: {ex.Message}");
        }

        await ExportAsync(report, fullTargetClientRoot).ConfigureAwait(false);
        return report;
    }

    public async Task<string> ExportAsync(UpkDeploymentReport report, string outputRoot)
    {
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(fullOutputRoot);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string jsonPath = Path.Combine(fullOutputRoot, $"ClientMapDeployment_{stamp}.json");
        string markdownPath = Path.Combine(fullOutputRoot, $"ClientMapDeployment_{stamp}.md");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(report)).ConfigureAwait(false);

        return fullOutputRoot;
    }

    public async Task<string> RefreshPackageIndexAsync(string cookedRoot, Action<string>? log)
    {
        string fullCookedRoot = Path.GetFullPath(cookedRoot);
        string dataDirectory = Path.Combine(fullCookedRoot, "Data");
        Directory.CreateDirectory(dataDirectory);

        string sqlitePath = Path.Combine(dataDirectory, "mh152upk.db");
        string mpkPath = Path.Combine(dataDirectory, "mh152.mpk");
        if (File.Exists(sqlitePath))
            File.Delete(sqlitePath);

        if (File.Exists(mpkPath))
            File.Delete(mpkPath);

        string[] upkFiles = Directory
            .EnumerateFiles(fullCookedRoot, "*.upk", SearchOption.TopDirectoryOnly)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (upkFiles.Length == 0)
            return mpkPath;

        UpkIndexingSystem.DbPath = sqlitePath;
        IUpkFileRepository repository = new UpkFileRepository();

        await UpkIndexingSystem.InitializeDatabaseAsync().ConfigureAwait(false);

        int totalFiles = upkFiles.Length;
        for (int i = 0; i < upkFiles.Length; i++)
        {
            string upk = upkFiles[i];
            log?.Invoke($"Index refresh imports {i + 1}/{totalFiles}: {Path.GetFileName(upk)}");
            await UpkIndexingSystem.CollectPackageImportsFromFileAsync(upk, repository, CancellationToken.None).ConfigureAwait(false);
        }

        for (int i = 0; i < upkFiles.Length; i++)
        {
            string upk = upkFiles[i];
            log?.Invoke($"Index refresh locations {i + 1}/{totalFiles}: {Path.GetFileName(upk)}");
            await UpkIndexingSystem.CollectObjectLocationsFromFileAsync(upk, repository, CancellationToken.None).ConfigureAwait(false);
        }

        UpkIndexingSystem.Convert(mpkPath);
        return mpkPath;
    }

    private static string BuildMarkdown(UpkDeploymentReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# ClientMap Deployment Report");
        builder.AppendLine();
        builder.AppendLine($"- Source UPK: {report.SourceUpkPath}");
        builder.AppendLine($"- Target root: {report.TargetClientRoot}");
        builder.AppendLine($"- Deploy file: {report.DeployFileName}");
        builder.AppendLine($"- Deployed path: {report.DeployedPath}");
        builder.AppendLine($"- ClientMap name: {report.ClientMapName}");
        builder.AppendLine($"- Refresh package index: {report.RefreshPackageIndex}");
        builder.AppendLine($"- Package index path: {report.PackageIndexPath ?? "(none)"}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine(report.SummaryText);
        builder.AppendLine();
        builder.AppendLine("## Notes");
        foreach (string note in report.Notes)
            builder.AppendLine($"- {note}");
        if (report.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            foreach (string warning in report.Warnings)
                builder.AppendLine($"- {warning}");
        }
        if (report.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Errors");
            foreach (string error in report.Errors)
                builder.AppendLine($"- {error}");
        }

        return builder.ToString();
    }
}
