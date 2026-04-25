using System.IO;
using System.Reflection;
using System.Text.Json;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk152Builder;

public sealed class Upk152Writer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly UpkFileRepository repository = new();

    public async Task<MigrationResult> WriteAsync(MigrationPackagePlan plan, Action<string>? log = null)
    {
        if (plan.SourceDocument is null)
            throw new InvalidOperationException("Migration plan is missing a source document.");

        if (plan.ValidationIssues.Any(issue => issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Migration plan contains validation errors.");

        string outputDirectory = Path.GetDirectoryName(plan.Header.OutputPath) ?? Path.GetDirectoryName(plan.SourceDocument.SourcePath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(outputDirectory);

        string outputPath = string.IsNullOrWhiteSpace(plan.Header.OutputPath)
            ? Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(plan.SourceDocument.SourcePath)}_152.upk")
            : plan.Header.OutputPath;
        string tempOutputPath = outputPath + ".tmp";
        string canonicalTempOutputPath = tempOutputPath + ".repair";

        try
        {
            await WriteValidatedPackageAsync(plan.SourceDocument.RawHeader, plan.Header, tempOutputPath, canonicalTempOutputPath, log).ConfigureAwait(false);

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            File.Move(tempOutputPath, outputPath);
        }
        catch
        {
            if (File.Exists(tempOutputPath))
                File.Delete(tempOutputPath);

            if (File.Exists(canonicalTempOutputPath))
                File.Delete(canonicalTempOutputPath);

            throw;
        }

        string manifestPath = Path.ChangeExtension(outputPath, ".migration.json");
        UpkMigrationManifest manifest = new()
        {
            SourceUpkPath = plan.SourceDocument.SourcePath,
            OutputUpkPath = outputPath,
            SchemaVersion = UpkMigrationSchemaManager.CurrentVersion.Value,
            SourceVersion = plan.Header.SourceVersion,
            TargetVersion = plan.Header.TargetVersion,
            MeshCount = plan.Meshes.Count,
            TextureCount = plan.Textures.Count,
            AnimationCount = plan.Animations.Count,
            MaterialCount = plan.Materials.Count,
            ReferenceCount = plan.References.Count,
            ValidationIssueCount = plan.ValidationIssues.Count,
            GraphNodeCount = plan.GraphNodes.Count,
            GraphEdgeCount = plan.GraphEdges.Count,
            Warnings = plan.Warnings.ToList()
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions)).ConfigureAwait(false);
        log?.Invoke($"Migration manifest written: {manifestPath}");

        return new MigrationResult
        {
            InputUpkPath = plan.SourceDocument.SourcePath,
            OutputUpkPath = outputPath,
            SchemaVersion = UpkMigrationSchemaManager.CurrentVersion.Value,
            AnalyzerVersion = UpkMigrationVersions.AnalyzerVersion,
            SourceFingerprint = UpkMigrationCache.ComputeFingerprint(plan.SourceDocument.SourcePath),
            MigratedMeshes = plan.Meshes.Count,
            MigratedTextures = plan.Textures.Count,
            MigratedAnimations = plan.Animations.Count,
            MigratedMaterials = plan.Materials.Count
        };
    }

    private static void PrepareHeader(UpkManager.Models.UpkFile.UnrealHeader header, Upk152Header target, string outputPath)
    {
        header.FullFilename = outputPath;
        SetProperty(header, "Version", target.TargetVersion);
        SetProperty(header, "Licensee", target.Licensee);
        SetProperty(header, "Flags", target.Flags);
        SetProperty(header, "EngineVersion", target.EngineVersion);
        SetProperty(header, "CookerVersion", target.CookerVersion);
        SetProperty(header, "CompressionFlags", 0U);

        PropertyInfo? compressedChunksProperty = header.GetType().GetProperty("CompressedChunks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (compressedChunksProperty?.GetValue(header) is System.Collections.IList compressedChunks)
            compressedChunks.Clear();
    }

    private async Task ValidateWrittenPackageAsync(string outputPath, Action<string>? log)
    {
        log?.Invoke($"Validating migrated UPK: {outputPath}");

        UpkManager.Models.UpkFile.UnrealHeader validationHeader = await repository.LoadUpkFile(outputPath).ConfigureAwait(false);
        await validationHeader.ReadDependsTableAsync(null).ConfigureAwait(false);

        log?.Invoke("Migrated UPK validation passed.");
    }

    private async Task WriteValidatedPackageAsync(UpkManager.Models.UpkFile.UnrealHeader header, Upk152Header target, string tempOutputPath, string canonicalTempOutputPath, Action<string>? log)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                PrepareHeader(header, target, tempOutputPath);
                log?.Invoke($"Writing new UPK package (attempt {attempt}/2): {tempOutputPath}");

                if (File.Exists(tempOutputPath))
                    File.Delete(tempOutputPath);

                header.FullFilename = tempOutputPath;
                await repository.SaveUpkFile(header, tempOutputPath, log).ConfigureAwait(false);
                await ValidateWrittenPackageAsync(tempOutputPath, log).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Repair attempt {attempt}/2 failed: {ex.Message}");

                if (File.Exists(tempOutputPath))
                    File.Delete(tempOutputPath);

                if (File.Exists(canonicalTempOutputPath))
                    File.Delete(canonicalTempOutputPath);
            }
        }

        try
        {
            log?.Invoke($"Attempting canonical re-save: {tempOutputPath}");

            UpkManager.Models.UpkFile.UnrealHeader canonicalHeader = await repository.LoadUpkFile(tempOutputPath).ConfigureAwait(false);
            await canonicalHeader.ReadDependsTableAsync(null).ConfigureAwait(false);

            canonicalHeader.FullFilename = canonicalTempOutputPath;
            if (File.Exists(canonicalTempOutputPath))
                File.Delete(canonicalTempOutputPath);

            await repository.SaveUpkFile(canonicalHeader, canonicalTempOutputPath, log).ConfigureAwait(false);
            await ValidateWrittenPackageAsync(canonicalTempOutputPath, log).ConfigureAwait(false);

            if (File.Exists(tempOutputPath))
                File.Delete(tempOutputPath);

            File.Move(canonicalTempOutputPath, tempOutputPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Migrated UPK could not be repaired into a valid package.", ex);
        }
    }

    private static void SetProperty<T>(object instance, string propertyName, T value)
    {
        PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property?.SetValue(instance, value);
    }
}

public sealed class UpkMigrationManifest
{
    public string SourceUpkPath { get; set; } = string.Empty;
    public string OutputUpkPath { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public ushort SourceVersion { get; set; }
    public ushort TargetVersion { get; set; }
    public int MeshCount { get; set; }
    public int TextureCount { get; set; }
    public int AnimationCount { get; set; }
    public int MaterialCount { get; set; }
    public int ReferenceCount { get; set; }
    public int ValidationIssueCount { get; set; }
    public int GraphNodeCount { get; set; }
    public int GraphEdgeCount { get; set; }
    public List<string> Warnings { get; set; } = [];
}

