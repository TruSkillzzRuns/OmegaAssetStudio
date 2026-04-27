using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using OmegaAssetStudio.WinUI;
using OmegaAssetStudio.TextureManager;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk152Builder;
using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.ThanosMigration.Services;
using UpkManager.Models.UpkFile.Engine.Texture;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkMigrationService
{
    private static readonly object LogFileLock = new();
    private static readonly string LogDirectoryPath = RuntimeLogPaths.UpkMigrationLogDirectory;
    private static readonly string LogFilePath = RuntimeLogPaths.UpkMigrationLogPath;
    private readonly DispatcherQueue? dispatcherQueue;
    private readonly Upk148Reader.Upk148Reader reader = new();
    private readonly Upk148AssetExtractor extractor = new();
    private readonly MeshConverter148ToNeutral meshConverter = new();
    private readonly TextureConverter148ToNeutral textureConverter = new();
    private readonly AnimationConverter148ToNeutral animationConverter = new();
    private readonly MaterialConverter148ToNeutral materialConverter = new();
    private readonly Upk152AssetInjector injector = new();
    private readonly Upk152Writer writer = new();
    private readonly UpkMigrationCache cache = new();
    private readonly UpkReferenceResolver referenceResolver = new();
    private readonly UpkGraphSanityChecker graphSanityChecker = new();
    private readonly ThanosStructuralMigrationService thanosStructuralService;
    private readonly ThanosTextureMigrationService thanosTextureService;
    private readonly TfcManifestService tfcManifestService;
    private readonly List<MigrationLogEntry> logEntries = [];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public event Action<MigrationLogEntry>? LogEntryAdded;

    public event Action<double, string>? ProgressChanged;

    public event Action<string>? StatusChanged;

    public event Action<MigrationJob>? JobUpdated;

    public string StatusText { get; set; } = "Ready.";

    public string ProgressText { get; private set; } = "Ready.";

    public double OverallProgress { get; private set; }

    public string LastRunSummaryText { get; private set; } = "No migration has run yet.";

    public IReadOnlyList<MigrationLogEntry> LogEntries => logEntries;

    public UpkMigrationService(
        DispatcherQueue? dispatcherQueue = null,
        ThanosStructuralMigrationService? thanosStructuralService = null,
        ThanosTextureMigrationService? thanosTextureService = null,
        TfcManifestService? tfcManifestService = null)
    {
        this.dispatcherQueue = dispatcherQueue;
        this.thanosStructuralService = thanosStructuralService ?? new();
        this.thanosTextureService = thanosTextureService ?? new();
        this.tfcManifestService = tfcManifestService ?? new();
        Directory.CreateDirectory(LogDirectoryPath);
        AppendLogFileLine("=== UPK Migration service initialized ===");
    }

    public void ClearCache()
    {
        cache.Clear();
        UpdateStatus("Migration cache cleared.");
        AppendLogFileLine("[INFO] [SERVICE] Migration cache cleared.");
    }

    public async Task RunMigrationAsync(IEnumerable<MigrationJob> jobs, string outputDirectory, string? textureManifestDirectory = null)
    {
        List<MigrationJob> jobList = jobs.ToList();
        if (jobList.Count == 0)
        {
            UpdateStatus("No migration jobs queued.");
            UpdateSummary(jobList);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Choose an output directory before starting migration.");

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await Task.Run(async () =>
            {
            int completed = 0;
            int totalMeshes = 0;
            int totalTextures = 0;
            int totalAnimations = 0;
            int totalMaterials = 0;
            int totalWarnings = 0;
            int totalErrors = 0;

            foreach (MigrationJob job in jobList)
            {
                await UpdateJobAsync(job, state =>
                {
                    state.Status = MigrationJobStatus.Running;
                    state.CurrentStep = job.IsThanosRaid ? "Analyzing..." : "Queued";
                    state.WarningCount = 0;
                    state.ErrorCount = 0;
                    state.Result = null;
                    state.AnalyzeProgress = 0;
                    state.MigrateProgress = 0;
                }).ConfigureAwait(false);

                MigrationResult result = new()
                {
                    InputUpkPath = job.SourceUpkPath,
                    OutputUpkPath = BuildOutputPath(outputDirectory, job.SourceUpkPath)
                };
                string migrationInputPath = job.SourceUpkPath;
                string sourceRootPath = Path.GetDirectoryName(Path.GetFullPath(job.SourceUpkPath))
                    ?? Path.GetPathRoot(Path.GetFullPath(job.SourceUpkPath))
                    ?? string.Empty;

                if (job.IsThanosRaid)
                {
                    string structuralOutputPath = Path.Combine(
                        outputDirectory,
                        $"{Path.GetFileNameWithoutExtension(job.SourceUpkPath)}_thanos_structural.upk");

                    QueueJobProgress(job, analyzeProgress: 0, currentStep: "Analyzing Thanos package...");
                    AddLog(job, $"Thanos structural patch output: {structuralOutputPath}");
                    IReadOnlyList<ThanosMigrationStep> structuralSteps = await thanosStructuralService.MigrateStructure(
                        job.SourceUpkPath,
                        structuralOutputPath,
                        (progress, message) => QueueJobProgress(job, analyzeProgress: progress * 0.5, currentStep: message)).ConfigureAwait(false);
                    foreach (ThanosMigrationStep step in structuralSteps)
                        AddLog(job, $"Thanos step {step.Name}: {step.Status}{(string.IsNullOrWhiteSpace(step.Reason) ? string.Empty : $" ({step.Reason})")}");

                    if (!structuralSteps.Any(static step => step.Status == ThanosMigrationStepStatus.Failed))
                    {
                        QueueJobProgress(job, analyzeProgress: 50, currentStep: "Analyzing Thanos textures...");
                    }
                }

                UpkMigrationReadOnlyContext readOnlyContext = new(sourceRootPath);
                readOnlyContext.EnsureReadableSource(job.SourceUpkPath);
                readOnlyContext.EnsureWritableOutput(result.OutputUpkPath);
                string sourceFingerprint = UpkMigrationCache.ComputeFingerprint(job.SourceUpkPath);
                string cacheKey = UpkMigrationCache.BuildCacheKey(job.SourceUpkPath);
                await UpdateJobAsync(job, state =>
                {
                    state.CacheKey = cacheKey;
                    state.SourceFingerprint = sourceFingerprint;
                    state.SchemaVersion = UpkMigrationSchemaManager.CurrentVersion.Value;
                    state.AnalyzerVersion = UpkMigrationVersions.AnalyzerVersion;
                }).ConfigureAwait(false);

                if (cache.TryGetValid(migrationInputPath, UpkMigrationSchemaManager.CurrentVersion.Value, UpkMigrationVersions.AnalyzerVersion, sourceFingerprint, out UpkMigrationCacheEntry? cacheEntry) && cacheEntry is not null)
                {
                    MigrationResult cachedResult = BuildCachedResult(cacheEntry);
                    cachedResult.Envelope = UpkMigrationSchemaManager.Wrap("UpkMigrationService", UpkMigrationSchemaManager.CreateSnapshot(cachedResult));
                    await UpdateJobAsync(job, state =>
                    {
                        state.OutputUpkPath = cachedResult.OutputUpkPath;
                        state.Result = cachedResult;
                        state.WarningCount = cachedResult.Warnings.Count;
                        state.ErrorCount = cachedResult.Errors.Count;
                        state.Status = MigrationJobStatus.Completed;
                        state.CurrentStep = "Cache hit";
                        state.AnalyzeProgress = 100;
                        state.MigrateProgress = 100;
                    }).ConfigureAwait(false);
                    AddLog(job, "Cache hit: migration skipped.");
                    completed++;
                    SetProgress((double)completed / Math.Max(1, jobList.Count) * 100.0, $"Migrated {completed} of {jobList.Count} UPK file(s).");
                    continue;
                }

                try
                {
                    if (job.IsThanosRaid)
                    {
                        AddLog(job, "Thanos Raid mode enabled: running structural and texture analysis.");
                        ThanosMigrationReport thanosReport = await thanosStructuralService.Analyze(
                            job.SourceUpkPath,
                            (progress, message) => QueueJobProgress(job, analyzeProgress: progress * 0.5, currentStep: message)).ConfigureAwait(false);
                        QueueJobProgress(job, analyzeProgress: 50, currentStep: "Analyzing Thanos textures...");
                        IReadOnlyList<ThanosMigrationFinding> textureFindings = await thanosTextureService.AnalyzeTextures(
                            job.SourceUpkPath,
                            (progress, message) => QueueJobProgress(job, analyzeProgress: 50 + (progress * 0.5), currentStep: message)).ConfigureAwait(false);
                        thanosReport.Findings.AddRange(textureFindings);
                        foreach (ThanosMigrationFinding finding in thanosReport.Findings)
                            AddLog(job, $"Thanos {finding.Severity} [{finding.Category}] {finding.Message}");
                        QueueJobProgress(job, analyzeProgress: 100, currentStep: "Thanos analysis complete.");
                    }

                    await UpdateJobAsync(job, state =>
                    {
                        state.CurrentStep = "Reading 1.48 UPK...";
                        state.MigrateProgress = 5;
                    }).ConfigureAwait(false);
                    AddLog(job, "Reading source package.");
                    Upk148Document document = await reader.ReadAsync(job.SourceUpkPath, message => AddLog(job, message), readOnlyContext).ConfigureAwait(false);

                    await UpdateJobAsync(job, state =>
                    {
                        state.CurrentStep = "Extracting assets...";
                        state.MigrateProgress = 25;
                    }).ConfigureAwait(false);
                    Upk148AssetBundle bundle = extractor.Extract(document, message => AddLog(job, message));

                    await UpdateJobAsync(job, state =>
                    {
                        state.MeshCount = bundle.SkeletalMeshes.Count + bundle.StaticMeshes.Count;
                        state.TextureCount = bundle.Textures.Count;
                        state.AnimationCount = bundle.Animations.Count;
                        state.MaterialCount = bundle.Materials.Count;
                        state.OutputUpkPath = result.OutputUpkPath;
                        state.MigrateProgress = 40;
                    }).ConfigureAwait(false);

                    List<MeshConversionResult> meshes = [];
                    foreach (Upk148ExportTableEntry entry in bundle.SkeletalMeshes.Concat(bundle.StaticMeshes))
                    {
                        try
                        {
                            meshes.AddRange(meshConverter.Convert(entry, message => AddLog(job, message)));
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Mesh conversion skipped for {entry.PathName}: {ex.Message}");
                            AddLog(job, $"Warning: mesh conversion skipped for {entry.PathName}: {ex.Message}");
                            AddLog(job, ex.ToString());
                        }
                    }

                    List<NeutralTexture> textures = [];
                    foreach (Upk148ExportTableEntry entry in bundle.Textures)
                    {
                        try
                        {
                            textures.AddRange(textureConverter.Convert(entry, message => AddLog(job, message)));
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Texture conversion skipped for {entry.PathName}: {ex.Message}");
                            AddLog(job, $"Warning: texture conversion skipped for {entry.PathName}: {ex.Message}");
                        }
                    }

                    List<NeutralAnimation> animations = [];
                    foreach (Upk148ExportTableEntry entry in bundle.Animations)
                    {
                        try
                        {
                            animations.AddRange(animationConverter.Convert(entry, message => AddLog(job, message)));
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Animation conversion skipped for {entry.PathName}: {ex.Message}");
                            AddLog(job, $"Warning: animation conversion skipped for {entry.PathName}: {ex.Message}");
                        }
                    }

                    List<NeutralMaterial> materials = [];
                    foreach (Upk148ExportTableEntry entry in bundle.Materials)
                    {
                        try
                        {
                            materials.AddRange(materialConverter.Convert(entry, message => AddLog(job, message)));
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Material conversion skipped for {entry.PathName}: {ex.Message}");
                            AddLog(job, $"Warning: material conversion skipped for {entry.PathName}: {ex.Message}");
                        }
                    }

                    result.MigratedMeshes = meshes.Count;
                    result.MigratedTextures = textures.Count;
                    result.MigratedAnimations = animations.Count;
                    result.MigratedMaterials = materials.Count;
                    result.Warnings.AddRange(bundle.Warnings);
                    await UpdateJobAsync(job, state =>
                    {
                        state.CurrentStep = "Validating references...";
                        state.MigrateProgress = 70;
                    }).ConfigureAwait(false);

                    IReadOnlyList<MigrationReferenceMatch> references = referenceResolver.Resolve(document, meshes, textures, animations, materials);
                    result.ReferenceMatches.AddRange(references);

                    graphSanityChecker.Run(result);
                    if (result.HasValidationErrors)
                        result.Errors.Add("Validation failed before write.");

                    await UpdateJobAsync(job, state =>
                    {
                        state.CurrentStep = "Writing new 1.52-compatible UPK...";
                        state.MigrateProgress = 85;
                    }).ConfigureAwait(false);
                    MigrationPackagePlan plan = injector.BuildPlan(document, meshes, textures, animations, materials, result, result.Warnings);
                    plan.References.AddRange(result.ReferenceMatches);
                    plan.ValidationIssues.AddRange(result.ValidationIssues);
                    plan.GraphNodes.AddRange(result.GraphNodes);
                    plan.GraphEdges.AddRange(result.GraphEdges);
                    MigrationResult written = await writer.WriteAsync(plan, message => AddLog(job, message)).ConfigureAwait(false);

                    List<ThanosTfcEntry> tfcEntries = BuildTfcEntries(migrationInputPath, bundle, textureManifestDirectory, message => AddLog(job, message));
                    if (tfcEntries.Count > 0)
                    {
                        string outputManifestPath = Path.Combine(outputDirectory, TextureManifest.ManifestName);
                        List<ThanosTfcEntry> existingEntries = File.Exists(outputManifestPath)
                            ? tfcManifestService.LoadManifest(outputManifestPath)
                            : [];
                        List<ThanosTfcEntry> mergedEntries = existingEntries.Count > 0
                            ? tfcManifestService.MergeEntries(existingEntries, tfcEntries)
                            : tfcEntries;
                        tfcManifestService.SaveManifest(outputManifestPath, mergedEntries);
                        AddLog(job, $"Texture manifest written: {outputManifestPath} ({mergedEntries.Count:N0} entry(s)).");
                    }
                    else
                    {
                        AddLog(job, "Texture manifest not written: no matching TFC entries were found.");
                    }

                    result.OutputUpkPath = written.OutputUpkPath;
                    result.Warnings.AddRange(written.Warnings);
                    result.Errors.AddRange(written.Errors);
                    result.MigratedMeshes = written.MigratedMeshes;
                    result.MigratedTextures = written.MigratedTextures;
                    result.MigratedAnimations = written.MigratedAnimations;
                    result.MigratedMaterials = written.MigratedMaterials;
                    result.SchemaVersion = UpkMigrationSchemaManager.CurrentVersion.Value;
                    result.AnalyzerVersion = UpkMigrationVersions.AnalyzerVersion;
                    result.CacheKey = cacheKey;
                    result.SourceFingerprint = sourceFingerprint;
                    result.Envelope = UpkMigrationSchemaManager.Wrap("UpkMigrationService", UpkMigrationSchemaManager.CreateSnapshot(result));

                    cache.Store(new UpkMigrationCacheEntry
                    {
                        SourcePath = job.SourceUpkPath,
                        SourceLength = new FileInfo(job.SourceUpkPath).Length,
                        SourceLastWriteUtc = new FileInfo(job.SourceUpkPath).LastWriteTimeUtc,
                        SchemaVersion = result.SchemaVersion,
                        AnalyzerVersion = result.AnalyzerVersion,
                        SourceFingerprint = sourceFingerprint,
                        CacheKey = cacheKey,
                        OutputPath = result.OutputUpkPath,
                        MigratedMeshes = result.MigratedMeshes,
                        MigratedTextures = result.MigratedTextures,
                        MigratedAnimations = result.MigratedAnimations,
                        MigratedMaterials = result.MigratedMaterials,
                        Warnings = result.Warnings.ToList(),
                        Errors = result.Errors.ToList()
                    });

                    await UpdateJobAsync(job, state =>
                    {
                        state.OutputUpkPath = written.OutputUpkPath;
                        state.Result = result;
                        state.WarningCount = result.Warnings.Count;
                        state.ErrorCount = result.Errors.Count;
                        state.Status = result.Success ? MigrationJobStatus.Completed : MigrationJobStatus.Failed;
                        state.CurrentStep = result.Success ? "Completed" : "Completed with errors";
                        state.MigrateProgress = 100;
                    }).ConfigureAwait(false);

                    completed++;
                    totalMeshes += result.MigratedMeshes;
                    totalTextures += result.MigratedTextures;
                    totalAnimations += result.MigratedAnimations;
                    totalMaterials += result.MigratedMaterials;
                    totalWarnings += result.Warnings.Count;
                    totalErrors += result.Errors.Count;
                    AddLog(job, $"Migration completed. {result.SummaryText}");
                }
                catch (Exception ex)
                {
                    result.Errors.Add(ex.ToString());
                    result.ValidationIssues.Add(new MigrationValidationIssue
                    {
                        Severity = "Error",
                        Code = "MIGRATION_FAILED",
                        Message = ex.Message,
                        Source = job.SourceUpkPath,
                        Details = ex.ToString()
                    });
                    totalErrors++;
                    await UpdateJobAsync(job, state =>
                    {
                        state.OutputUpkPath = result.OutputUpkPath;
                        state.Result = result;
                        state.Status = MigrationJobStatus.Failed;
                        state.CurrentStep = "Failed";
                        state.WarningCount = result.Warnings.Count;
                        state.ErrorCount = result.Errors.Count;
                        state.MigrateProgress = 100;
                    }).ConfigureAwait(false);
                    AddLog(job, $"Migration failed: {ex}");
                }

                double progress = (double)completed / Math.Max(1, jobList.Count) * 100.0;
                SetProgress(progress, $"Migrated {completed} of {jobList.Count} UPK file(s).");
            }

            LastRunSummaryText =
                $"Jobs: {jobList.Count}{Environment.NewLine}" +
                $"Completed: {jobList.Count(job => job.Status == MigrationJobStatus.Completed)}{Environment.NewLine}" +
                $"Failed: {jobList.Count(job => job.Status == MigrationJobStatus.Failed)}{Environment.NewLine}" +
                $"Meshes: {totalMeshes}  Textures: {totalTextures}  Animations: {totalAnimations}  Materials: {totalMaterials}{Environment.NewLine}" +
                $"Warnings: {totalWarnings}  Errors: {totalErrors}";
            UpdateStatus("Migration run complete.");
            UpdateSummary(jobList);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Migration failed: {ex}");
            AppendLogFileLine($"[ERROR] [SERVICE] {ex}");
            throw;
        }
    }

    private async Task UpdateJobAsync(MigrationJob job, Action<MigrationJob> update)
    {
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            update(job);
            JobUpdated?.Invoke(job);
            return;
        }

        TaskCompletionSource tcs = new();
        dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                update(job);
                JobUpdated?.Invoke(job);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        await tcs.Task.ConfigureAwait(false);
    }

    private void QueueJobProgress(MigrationJob job, double? analyzeProgress = null, double? migrateProgress = null, string? currentStep = null)
    {
        _ = UpdateJobAsync(job, state =>
        {
            if (analyzeProgress.HasValue)
                state.AnalyzeProgress = Math.Clamp(analyzeProgress.Value, 0.0, 100.0);

            if (migrateProgress.HasValue)
                state.MigrateProgress = Math.Clamp(migrateProgress.Value, 0.0, 100.0);

            if (!string.IsNullOrWhiteSpace(currentStep))
                state.CurrentStep = currentStep;
        });
    }

    private void AddLog(MigrationJob job, string message)
    {
        string sourceName = Path.GetFileName(job.SourceUpkPath);
        MigrationLogEntry entry = new()
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Level = "Info",
            Code = "INFO",
            Source = sourceName,
            Message = message
        };
        logEntries.Add(entry);
        LogEntryAdded?.Invoke(entry);
        AppendLogFileLine($"[{entry.Timestamp}] [{entry.Level}] [{sourceName}] {message}");
    }

    private void SetProgress(double progress, string text)
    {
        OverallProgress = Math.Clamp(progress, 0.0, 100.0);
        ProgressText = text;
        ProgressChanged?.Invoke(OverallProgress, ProgressText);
    }

    private void UpdateStatus(string text)
    {
        StatusText = text;
        StatusChanged?.Invoke(text);
    }

    private void UpdateSummary(IEnumerable<MigrationJob> jobs)
    {
        if (jobs is null)
            return;
    }

    private static string BuildOutputPath(string outputDirectory, string sourcePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(outputDirectory, $"{fileName}_152.upk");
    }

    private static MigrationResult BuildCachedResult(UpkMigrationCacheEntry entry)
    {
        return new MigrationResult
        {
            InputUpkPath = entry.SourcePath,
            OutputUpkPath = entry.OutputPath,
            SchemaVersion = entry.SchemaVersion,
            AnalyzerVersion = entry.AnalyzerVersion,
            CacheKey = entry.CacheKey,
            SourceFingerprint = entry.SourceFingerprint,
            MigratedMeshes = entry.MigratedMeshes,
            MigratedTextures = entry.MigratedTextures,
            MigratedAnimations = entry.MigratedAnimations,
            MigratedMaterials = entry.MigratedMaterials
        };
    }

    private List<ThanosTfcEntry> BuildTfcEntries(string migrationInputPath, Upk148AssetBundle bundle, string? lookupManifestDirectory, Action<string>? log = null)
    {
        List<ThanosTfcEntry> entries = [];
        string packageName = Path.GetFileNameWithoutExtension(migrationInputPath);
        string sourceManifestPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(migrationInputPath)) ?? string.Empty, TextureManifest.ManifestName);
        string lookupManifestPath = string.Empty;
        int inspectedTextures = 0;
        int hydratedTextures = 0;
        int matchedTextures = 0;
        int missingManifestEntries = 0;
        int missingMipMaps = 0;
        List<string> missingManifestExamples = [];

        TextureManifest.Initialize();
        TextureManifest.Instance.Entries.Clear();
        bool sourceManifestExists = File.Exists(sourceManifestPath);
        bool lookupManifestExists = false;
        if (!string.IsNullOrWhiteSpace(lookupManifestDirectory))
        {
            lookupManifestPath = Path.Combine(Path.GetFullPath(lookupManifestDirectory), TextureManifest.ManifestName);
            lookupManifestExists = File.Exists(lookupManifestPath);
            if (lookupManifestExists)
                tfcManifestService.LoadManifest(lookupManifestPath);
        }

        if (!lookupManifestExists && sourceManifestExists)
        {
            lookupManifestPath = sourceManifestPath;
            lookupManifestExists = true;
            tfcManifestService.LoadManifest(sourceManifestPath);
        }

        if (!lookupManifestExists)
            log?.Invoke($"Info: texture manifest lookup file not found in target folder or source folder. Target lookup: {(string.IsNullOrWhiteSpace(lookupManifestDirectory) ? "(none)" : lookupManifestPath)}; source lookup: {sourceManifestPath}.");

        foreach (Upk148ExportTableEntry export in bundle.Textures)
        {
            inspectedTextures++;
            if (!UpkExportHydrator.TryHydrate<UTexture2D>(export, out _, log) &&
                export.RawExport.UnrealObject is null)
            {
                continue;
            }

            hydratedTextures++;
            object? textureObject = export.RawExport.UnrealObject;
            if (textureObject is null)
                continue;

            TextureEntry? textureEntry = textureObject is UTexture2D texture2D
                ? TextureManifest.Instance?.GetTextureEntry(texture2D)
                : null;

            if (textureEntry is null)
            {
                missingManifestEntries++;
                if (missingManifestExamples.Count < 5 && !missingManifestExamples.Contains(export.PathName, StringComparer.OrdinalIgnoreCase))
                    missingManifestExamples.Add(export.PathName);
                continue;
            }

            matchedTextures++;
            string textureName = textureEntry.Head.TextureName;
            Guid textureGuid = textureEntry.Head.TextureGuid;
            string tfcFileName = textureEntry.Data.TextureFileName ?? string.Empty;

            IReadOnlyList<TextureMipMap>? mipMaps = textureEntry.Data.Maps;
            if (mipMaps is null || mipMaps.Count == 0)
            {
                missingMipMaps++;
                log?.Invoke($"Info: texture manifest entry has no mip maps for {export.PathName}.");
                continue;
            }

            foreach (TextureMipMap map in mipMaps)
            {
                entries.Add(new ThanosTfcEntry
                {
                    PackageName = packageName,
                    TextureName = textureName,
                    TextureGuid = textureGuid,
                    TfcFileName = tfcFileName,
                    ChunkIndex = (int)map.Index,
                    Offset = map.Offset,
                    Size = map.Size
                });
            }
        }

        if (entries.Count == 0)
        {
            log?.Invoke(
                $"Texture manifest not written: inspected {inspectedTextures:N0} texture export(s), " +
                $"hydrated {hydratedTextures:N0}, matched {matchedTextures:N0}, " +
                $"missing manifest entries {missingManifestEntries:N0}, missing mip maps {missingMipMaps:N0}, " +
                $"lookup manifest present: {(lookupManifestExists ? "yes" : "no")}.");
        }
        else if (missingManifestEntries > 0)
        {
            string examples = missingManifestExamples.Count == 0
                ? string.Empty
                : $" Examples: {string.Join("; ", missingManifestExamples)}";
            log?.Invoke(
                $"Texture manifest lookup missed {missingManifestEntries:N0} texture entry(s); these were preserved for later manifest update.{examples}");
        }

        return entries;
    }

    private static void AppendLogFileLine(string line)
    {
        lock (LogFileLock)
        {
            Directory.CreateDirectory(LogDirectoryPath);
            File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}

