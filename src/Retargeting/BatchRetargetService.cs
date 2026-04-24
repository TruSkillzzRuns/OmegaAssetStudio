using System.Text;

namespace OmegaAssetStudio.Retargeting;

public sealed class BatchRetargetProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class BatchRetargetEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public sealed class BatchRetargetReport
{
    public string FolderPath { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public List<BatchRetargetEntry> Entries { get; } = [];

    public int SuccessCount => Entries.Count(entry => entry.Status.Equals("Success", StringComparison.OrdinalIgnoreCase));
    public int FailureCount => Entries.Count(entry => entry.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
}

public sealed class BatchRetargetService
{
    public async Task<BatchRetargetReport> RetargetFolderAsync(
        string folderPath,
        string skeletalMeshExportPath,
        RetargetMesh retargetedMesh,
        int lodIndex,
        bool replaceAllLods,
        string logDirectory,
        Action<string>? log = null,
        IProgress<BatchRetargetProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Folder path is required.", nameof(folderPath));
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Batch retarget folder not found: {folderPath}");
        if (string.IsNullOrWhiteSpace(skeletalMeshExportPath))
            throw new ArgumentException("A SkeletalMesh export path is required.", nameof(skeletalMeshExportPath));
        if (retargetedMesh is null)
            throw new ArgumentNullException(nameof(retargetedMesh));

        List<string> files = Directory
            .EnumerateFiles(folderPath, "*.upk", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException($"No UPK files were found under '{folderPath}'.");

        Directory.CreateDirectory(logDirectory);
        string logPath = Path.Combine(logDirectory, $"RetargetBatch_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        BatchRetargetReport report = new()
        {
            FolderPath = folderPath,
            LogPath = logPath
        };

        StringBuilder logText = new();
        logText.AppendLine($"Batch retarget started: {DateTime.Now:O}");
        logText.AppendLine($"Folder: {folderPath}");
        logText.AppendLine($"Export: {skeletalMeshExportPath}");
        logText.AppendLine($"LOD: {lodIndex}");
        logText.AppendLine($"Replace all LODs: {replaceAllLods}");
        logText.AppendLine();

        for (int index = 0; index < files.Count; index++)
        {
            string file = files[index];
            int displayIndex = index + 1;
            progress?.Report(new BatchRetargetProgress
            {
                Processed = index,
                Total = files.Count,
                CurrentFile = file,
                Status = $"Retargeting {Path.GetFileName(file)} ({displayIndex}/{files.Count})"
            });

            try
            {
                MeshReplacer replacer = new();
                string backupPath = await replacer.ReplaceMeshInUpkAsync(
                    file,
                    skeletalMeshExportPath,
                    retargetedMesh.DeepClone(),
                    lodIndex,
                    replaceAllLods,
                    log).ConfigureAwait(false);

                report.Entries.Add(new BatchRetargetEntry
                {
                    FilePath = file,
                    Status = "Success",
                    BackupPath = backupPath
                });

                logText.AppendLine($"[{displayIndex}/{files.Count}] SUCCESS {file}");
                logText.AppendLine($"  Backup: {backupPath}");
            }
            catch (Exception ex)
            {
                report.Entries.Add(new BatchRetargetEntry
                {
                    FilePath = file,
                    Status = "Failed",
                    Error = ex.Message
                });

                logText.AppendLine($"[{displayIndex}/{files.Count}] FAILED {file}");
                logText.AppendLine($"  Error: {ex.Message}");
            }
        }

        progress?.Report(new BatchRetargetProgress
        {
            Processed = files.Count,
            Total = files.Count,
            CurrentFile = string.Empty,
            Status = $"Batch finished: {report.SuccessCount} succeeded, {report.FailureCount} failed."
        });

        logText.AppendLine();
        logText.AppendLine($"Finished: {DateTime.Now:O}");
        logText.AppendLine($"Succeeded: {report.SuccessCount}");
        logText.AppendLine($"Failed: {report.FailureCount}");
        await File.WriteAllTextAsync(logPath, logText.ToString()).ConfigureAwait(false);

        return report;
    }
}

