using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UpkManager.Models.UpkFile.Engine.Anim;
using UpkManager.Models.UpkFile.Engine.MarvelGame;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;
using OmegaAssetStudio.TextureManager;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

internal sealed class OmegaIntelAnalyzerService
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] ExecutablePatterns =
    [
        "UPK",
        "TextureFileCache",
        "Texture2D",
        "SkeletalMesh",
        "AnimSet",
        "GameApprox",
        "Marvel",
        "Omega",
        "Retarget",
        "UIEditor",
    ];

    private static readonly Regex HeroIdRegex = new(@"\bHero(?:ID|Id|_id)\b\s*[:=]\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RosterRegex = new(@"\bRoster\b.{0,80}?\b([A-Za-z0-9_\-]{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerTreeRegex = new(@"\bPower(?:Tree|Set|Grid)\b.{0,80}?\b([A-Za-z0-9_\-]{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrintableStringRegex = new(@"[\x20-\x7E]{4,}", RegexOptions.Compiled);
    private static readonly string[] UiHeroKeywords =
    [
        "heroselect",
        "characterselect",
        "rosterpanel",
        "characterunlock",
        "!uisource",
        "portrait",
        "buttonroster",
        "buttonstore",
        "teamuppanel",
    ];
    private static readonly string[] FunctionSignaturePatterns =
    [
        "StaticLoadObject",
        "LoadObject",
        "FindObject",
        "ProcessEvent",
        "CreatePackage",
        "TextureFileCache",
        "SkeletalMesh",
        "AnimSet",
        "Texture2D",
        "HeroId",
        "Roster",
        "PowerTree",
        "UI",
        "CRC",
    ];
    private static readonly string[] StringTableKeywords =
    [
        "hero",
        "roster",
        "power",
        "select",
        "unlock",
        "portrait",
        "texture",
        "mesh",
        "anim",
        "cache",
        "package",
        "ui",
    ];

    private readonly UpkFileRepository upkRepository = new();

    public async Task<OmegaIntelScanResult> ScanAsync(OmegaIntelScanOptions options, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
            throw new ArgumentException("A root path is required.", nameof(options));

        string rootPath = Path.GetFullPath(options.RootPath);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        log ??= _ => { };
        log($"Starting scan: {rootPath}");
        Directory.CreateDirectory(OmegaIntelPaths.LogsDirectory);
        Directory.CreateDirectory(OmegaIntelPaths.CacheDirectory);
        Directory.CreateDirectory(OmegaIntelPaths.ReportsDirectory);

        OmegaIntelScanResult result = new()
        {
            RootPath = rootPath,
            StartedUtc = DateTime.UtcNow
        };

        TryAutoLoadTextureManifest(rootPath, result, log);

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootPath, "*", enumerationOptions).ToList();
        }
        catch (Exception ex)
        {
            log($"Failed to enumerate files: {ex.Message}");
            throw;
        }

        result.TotalFiles = files.Count;
        log($"Discovered {files.Count:N0} files.");

        foreach (string path in files)
        {
            OmegaIntelFileRecord record = await AnalyzeFileAsync(path, result, options, log).ConfigureAwait(false);
            result.Files.Add(record);
            result.ClassifiedFiles++;

            if (result.ClassifiedFiles % 1000 == 0 || result.ClassifiedFiles == result.TotalFiles)
                log($"Progress: {result.ClassifiedFiles:N0}/{result.TotalFiles:N0} files processed.");

            switch (record.Kind)
            {
                case OmegaIntelFileKind.Upk:
                    result.UpkFiles++;
                    break;
                case OmegaIntelFileKind.Tfc:
                    result.TfcFiles++;
                    break;
                case OmegaIntelFileKind.Texture:
                    result.TextureFiles++;
                    break;
                case OmegaIntelFileKind.Mesh:
                    result.MeshFiles++;
                    break;
                case OmegaIntelFileKind.Animation:
                    result.AnimationFiles++;
                    break;
                case OmegaIntelFileKind.Character:
                    result.CharacterFiles++;
                    break;
                case OmegaIntelFileKind.Ui:
                    result.UiFiles++;
                    break;
                case OmegaIntelFileKind.Executable:
                    result.ExecutableFiles++;
                    break;
                case OmegaIntelFileKind.Config:
                    result.ConfigFiles++;
                    break;
                case OmegaIntelFileKind.Script:
                    result.ScriptFiles++;
                    break;
                case OmegaIntelFileKind.Library:
                    result.LibraryFiles++;
                    break;
                case OmegaIntelFileKind.Data:
                    result.DataFiles++;
                    break;
                default:
                    result.UnknownFiles++;
                    break;
            }
        }

        BuildDirectoryRecords(result);
        RunValidation(result, log);
        result.FinishedUtc = DateTime.UtcNow;
        result.Summary = BuildSummary(result);
        if (options.BuildKnowledgeGraph)
            new OmegaIntelKnowledgeGraphService().BuildGraph(result);

        await File.WriteAllTextAsync(
            OmegaIntelPaths.LatestScanCachePath,
            JsonSerializer.Serialize(result, CacheJsonOptions)).ConfigureAwait(false);

        log($"Completed scan: {result.Summary}");
        log($"Graph nodes={result.Nodes.Count:N0}, edges={result.Edges.Count:N0}");
        return result;
    }

    public async Task<OmegaIntelScanResult> ScanUpkAsync(string upkPath, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("A UPK path is required.", nameof(upkPath));

        string fullPath = Path.GetFullPath(upkPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(fullPath);

        log ??= _ => { };
        log($"Starting UPK scan: {fullPath}");
        Directory.CreateDirectory(OmegaIntelPaths.LogsDirectory);
        Directory.CreateDirectory(OmegaIntelPaths.CacheDirectory);
        Directory.CreateDirectory(OmegaIntelPaths.ReportsDirectory);

        string rootPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
        OmegaIntelScanResult result = new()
        {
            RootPath = rootPath,
            StartedUtc = DateTime.UtcNow,
            TotalFiles = 1,
            Notes = $"UPK scan target: {fullPath}"
        };

        TryAutoLoadTextureManifest(rootPath, result, log);

        OmegaIntelScanOptions options = new()
        {
            RootPath = rootPath,
            DeepUpkAnalysis = true,
            AnalyzeExecutables = false,
            AnalyzeUpkFiles = true,
            AnalyzeTfcFiles = false,
            AnalyzeTextureFiles = false,
            AnalyzeMeshFiles = false,
            AnalyzeAnimationFiles = false,
            AnalyzeCharacterFiles = false,
            AnalyzeUiFiles = false,
            BuildKnowledgeGraph = true
        };

        OmegaIntelFileRecord record = await AnalyzeFileAsync(fullPath, result, options, log).ConfigureAwait(false);
        result.Files.Add(record);
        result.ClassifiedFiles = 1;

        switch (record.Kind)
        {
            case OmegaIntelFileKind.Upk:
                result.UpkFiles = 1;
                break;
            case OmegaIntelFileKind.Tfc:
                result.TfcFiles = 1;
                break;
            case OmegaIntelFileKind.Texture:
                result.TextureFiles = 1;
                break;
            case OmegaIntelFileKind.Mesh:
                result.MeshFiles = 1;
                break;
            case OmegaIntelFileKind.Animation:
                result.AnimationFiles = 1;
                break;
            case OmegaIntelFileKind.Character:
                result.CharacterFiles = 1;
                break;
            case OmegaIntelFileKind.Ui:
                result.UiFiles = 1;
                break;
            case OmegaIntelFileKind.Executable:
                result.ExecutableFiles = 1;
                break;
            case OmegaIntelFileKind.Config:
                result.ConfigFiles = 1;
                break;
            case OmegaIntelFileKind.Script:
                result.ScriptFiles = 1;
                break;
            case OmegaIntelFileKind.Library:
                result.LibraryFiles = 1;
                break;
            case OmegaIntelFileKind.Data:
                result.DataFiles = 1;
                break;
            default:
                result.UnknownFiles = 1;
                break;
        }

        BuildDirectoryRecords(result);
        RunValidation(result, log);
        result.FinishedUtc = DateTime.UtcNow;
        result.Summary = BuildSummary(result);

        if (options.BuildKnowledgeGraph)
            new OmegaIntelKnowledgeGraphService().BuildGraph(result);

        await File.WriteAllTextAsync(
            OmegaIntelPaths.LatestScanCachePath,
            JsonSerializer.Serialize(result, CacheJsonOptions)).ConfigureAwait(false);

        log($"Completed UPK scan: {result.Summary}");
        log($"Graph nodes={result.Nodes.Count:N0}, edges={result.Edges.Count:N0}");
        return result;
    }

    private async Task<OmegaIntelFileRecord> AnalyzeFileAsync(string path, OmegaIntelScanResult result, OmegaIntelScanOptions options, Action<string> log)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        FileInfo info = new(path);

        OmegaIntelFileRecord record = new()
        {
            Path = path,
            Directory = Path.GetDirectoryName(path) ?? string.Empty,
            Name = Path.GetFileName(path),
            Extension = extension,
            SizeBytes = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc
        };

        byte[] sample = await ReadSampleAsync(path).ConfigureAwait(false);
        record.MagicBytes = FormatMagicBytes(sample);
        record.Entropy = CalculateEntropy(sample);
        if (record.Entropy >= 7.5 && record.SizeBytes >= 1024)
            result.HighEntropyFiles++;
        if (string.IsNullOrWhiteSpace(record.MagicBytes) || record.MagicBytes == "00 00 00 00")
            result.UnknownMagicFiles++;

        record.Kind = ClassifyByExtension(extension);
        record.Tags = BuildTags(record.Kind, extension).ToArray();

        switch (record.Kind)
        {
            case OmegaIntelFileKind.Upk:
                if (options.AnalyzeUpkFiles)
                    await AnalyzeUpkAsync(path, record, result, options, log).ConfigureAwait(false);
                break;
            case OmegaIntelFileKind.Tfc:
                if (options.AnalyzeTfcFiles)
                    AnalyzeTfc(record, result, log);
                break;
            case OmegaIntelFileKind.Texture:
                if (options.AnalyzeTextureFiles)
                    AnalyzeTexture(record);
                break;
            case OmegaIntelFileKind.Mesh:
                if (options.AnalyzeMeshFiles)
                    AnalyzeMesh(record);
                break;
            case OmegaIntelFileKind.Animation:
                if (options.AnalyzeAnimationFiles)
                    AnalyzeAnimation(record);
                break;
            case OmegaIntelFileKind.Character:
                if (options.AnalyzeCharacterFiles)
                    AnalyzeCharacter(record);
                break;
            case OmegaIntelFileKind.Ui:
                if (options.AnalyzeUiFiles)
                    AnalyzeUi(path, record, result, log);
                break;
            case OmegaIntelFileKind.Executable:
                if (options.AnalyzeExecutables)
                    AnalyzeExecutable(path, record, result);
                break;
            case OmegaIntelFileKind.Config:
                AnalyzeConfig(record);
                break;
            case OmegaIntelFileKind.Script:
                AnalyzeScript(record);
                break;
            case OmegaIntelFileKind.Library:
                AnalyzeLibrary(record);
                break;
            default:
                AnalyzeUnknown(record);
                break;
        }

        return record;
    }

    private async Task AnalyzeUpkAsync(string path, OmegaIntelFileRecord record, OmegaIntelScanResult? result, OmegaIntelScanOptions options, Action<string> log)
    {
        try
        {
            var header = await upkRepository.LoadUpkFile(path).ConfigureAwait(false);
            await header.ReadHeaderAsync(null).ConfigureAwait(false);

            var exports = header.ExportTable;
            var groups = exports
                .GroupBy(export => export.ClassReferenceNameIndex?.Name ?? export.ClassReferenceNameIndex?.ToString() ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .ToList();

            var topGroups = groups.Take(6).ToList();
            string classification = topGroups.Count == 0
                ? "UPK"
                : $"UPK / {string.Join(", ", topGroups.Select(group => $"{group.Key} x{group.Count()}"))}";

            record.Classification = classification;
            record.Summary = exports.Count == 0
                ? "No exports resolved."
                : $"{exports.Count:N0} exports; top: {string.Join(", ", topGroups.Select(group => $"{group.Key} x{group.Count()}"))}";
            record.Details = $"Class groups: {groups.Count:N0}.";

            List<OmegaIntelInsight> insights = [];
            foreach (var group in groups.Take(12))
            {
                insights.Add(new OmegaIntelInsight
                {
                    Kind = "ExportClass",
                    Value = $"{group.Key} x{group.Count()}",
                    Details = $"UPK export class in {Path.GetFileName(path)}"
                });
            }

            if (options.DeepUpkAnalysis)
            {
                int textureExports = CountExports(exports, "Texture2D");
                int skeletalMeshes = CountExports(exports, "SkeletalMesh");
                int staticMeshes = CountExports(exports, "StaticMesh");
                int animSets = CountExports(exports, "AnimSet");
                int uiExports = CountExports(exports, "UIScene") + CountExports(exports, "UIStyle");
                int textureLike = CountExportsMatching(exports, export => ExportMatches(export, "Texture2D", "TextureCube", "Material") || NameContains(export, "Texture"));
                int meshLike = CountExportsMatching(exports, export => ExportMatches(export, "SkeletalMesh", "StaticMesh", "SkeletalMeshComponent") || NameContains(export, "Mesh"));
                int animationLike = CountExportsMatching(exports, export => ExportMatches(export, "AnimSet", "AnimSequence", "AnimTree", "AnimNode") || NameContains(export, "Anim"));
                int uiLike = CountExportsMatching(exports, export => ExportMatches(export, "UIScene", "UIStyle", "UIObject", "UIRoot") || NameContains(export, "UI"));
                int characterLike = CountExportsMatching(exports, export => NameContains(export, "Character") || NameContains(export, "Hero") || NameContains(export, "Villain") || NameContains(export, "NPC"));

                if (textureExports > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Content", Value = $"Texture2D x{textureExports}", Details = "UPK contains texture exports." });
                if (skeletalMeshes > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Content", Value = $"SkeletalMesh x{skeletalMeshes}", Details = "UPK contains skeletal mesh exports." });
                if (staticMeshes > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Content", Value = $"StaticMesh x{staticMeshes}", Details = "UPK contains static mesh exports." });
                if (animSets > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Content", Value = $"AnimSet x{animSets}", Details = "UPK contains animation exports." });
                if (uiExports > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Content", Value = $"UI exports x{uiExports}", Details = "UPK contains UI-related exports." });
                if (textureLike > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Profile", Value = $"Texture-heavy x{textureLike}", Details = "UPK has texture-centric exports or names." });
                if (meshLike > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Profile", Value = $"Mesh-heavy x{meshLike}", Details = "UPK has mesh-centric exports or names." });
                if (animationLike > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Profile", Value = $"Animation-heavy x{animationLike}", Details = "UPK has animation-centric exports or names." });
                if (uiLike > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Profile", Value = $"UI-heavy x{uiLike}", Details = "UPK has UI-centric exports or names." });
                if (characterLike > 0)
                    insights.Add(new OmegaIntelInsight { Kind = "Profile", Value = $"Character-heavy x{characterLike}", Details = "UPK has character-centric exports or names." });

                var profileTags = new List<string>();
                if (textureLike > 0) profileTags.Add("TextureHeavy");
                if (meshLike > 0) profileTags.Add("MeshHeavy");
                if (animationLike > 0) profileTags.Add("AnimationHeavy");
                if (uiLike > 0) profileTags.Add("UIHeavy");
                if (characterLike > 0) profileTags.Add("CharacterHeavy");
                if (profileTags.Count > 0)
                    record.Tags = record.Tags.Concat(profileTags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                int resolvedTextures = 0;
                int cacheBackedTextures = 0;
                int resolvedMeshes = 0;
                int resolvedAnimSets = 0;
                int resolvedCharacters = 0;
                int animSequenceTotal = 0;
                int skeletalBoneTotal = 0;

                foreach (var export in exports.Where(IsInterestingContentExport).Take(80))
                {
                    if (export.UnrealObject == null)
                    {
                        try
                        {
                            await export.ParseUnrealObject(false, false).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            insights.Add(new OmegaIntelInsight
                            {
                                Kind = "ParseError",
                                Value = export.GetPathName(),
                                Details = ex.Message
                            });
                            continue;
                        }
                    }

                    if (export.UnrealObject is not IUnrealObject unrealObject)
                        continue;

                    switch (unrealObject.UObject)
                    {
                        case UTexture2D texture:
                            resolvedTextures++;
                            if (!string.IsNullOrWhiteSpace(texture.TextureFileCacheName?.Name))
                            {
                                cacheBackedTextures++;
                                string cacheName = texture.TextureFileCacheName.Name;
                                string directory = Path.GetDirectoryName(path) ?? string.Empty;
                                string tfcPath = Path.Combine(directory, cacheName + ".tfc");
                                if (result is not null)
                                {
                                    result.TfcMap.Add(new OmegaIntelTfcMapEntry
                                    {
                                        TextureName = export.GetPathName(),
                                        CacheName = cacheName,
                                        TfcPath = tfcPath,
                                        ManifestPath = FindManifestPath(directory),
                                        CacheFileExists = File.Exists(tfcPath),
                                        TextureFormat = texture.Format.ToString()
                                    });
                                }
                            }
                            insights.Add(new OmegaIntelInsight
                            {
                                Kind = "ResolvedTexture",
                                Value = texture.TextureFileCacheName?.Name ?? texture.Format.ToString(),
                                Details = $"LODGroup={texture.LODGroup}, Size={texture.SizeX}x{texture.SizeY}"
                            });
                            break;
                        case USkeletalMesh mesh:
                            resolvedMeshes++;
                            result!.ResolvedSkeletalMeshUpks++;
                            skeletalBoneTotal += mesh.RefSkeleton?.Count ?? 0;
                            insights.Add(new OmegaIntelInsight
                            {
                                Kind = "ResolvedMesh",
                                Value = mesh.RefSkeleton?.Count.ToString() ?? "0",
                                Details = $"Bones={mesh.RefSkeleton?.Count ?? 0}, LODs={mesh.LODModels?.Count ?? 0}, Materials={mesh.Materials?.Count ?? 0}"
                            });
                            break;
                        case UStaticMesh mesh:
                            resolvedMeshes++;
                            result!.ResolvedStaticMeshUpks++;
                            int staticLodCount = mesh.LODModels?.Count ?? 0;
                            int elementCount = mesh.SourceData?.RenderData?.Elements?.Count ?? 0;
                            insights.Add(new OmegaIntelInsight
                            {
                                Kind = "ResolvedMesh",
                                Value = mesh.HighResSourceMeshName ?? "StaticMesh",
                                Details = $"Static LODs={staticLodCount}, Elements={elementCount}, LightMap={mesh.LightMapResolution}"
                            });
                            break;
                        case UAnimSet animSet:
                            resolvedAnimSets++;
                            int sequenceCount = animSet.Sequences?.Count ?? 0;
                            animSequenceTotal += sequenceCount;
                            insights.Add(new OmegaIntelInsight
                            {
                                Kind = "ResolvedAnimSet",
                                Value = animSet.PreviewSkelMeshName?.Name ?? "AnimSet",
                                Details = $"Sequences={sequenceCount}, TrackBones={animSet.TrackBoneNames?.Count ?? 0}"
                            });
                            break;
                        case UMarvelEntity entity:
                            resolvedCharacters++;
                            int aliasCount = entity.AnimationSetAliases?.Count ?? 0;
                            insights.Add(new OmegaIntelInsight
                            {
                                Kind = "ResolvedCharacter",
                                Value = entity.GetType().Name,
                                Details = $"AnimAliases={aliasCount}, Attachments={entity.MAttachmentClasses?.Count ?? 0}"
                            });
                            break;
                    }
                }

                var resolvedTags = new List<string>();
                if (resolvedTextures > 0) resolvedTags.Add("ResolvedTexture");
                if (resolvedMeshes > 0) resolvedTags.Add("ResolvedMesh");
                if (resolvedAnimSets > 0) resolvedTags.Add("ResolvedAnimSet");
                if (resolvedCharacters > 0) resolvedTags.Add("ResolvedCharacter");
                if (cacheBackedTextures > 0) resolvedTags.Add("CacheBackedTexture");
                record.Tags = record.Tags.Concat(resolvedTags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                if (result is not null)
                {
                    result.ResolvedTextureUpks += resolvedTextures > 0 ? 1 : 0;
                    result.ResolvedMeshUpks += resolvedMeshes > 0 ? 1 : 0;
                    result.ResolvedAnimSetUpks += resolvedAnimSets > 0 ? 1 : 0;
                    result.ResolvedCharacterUpks += resolvedCharacters > 0 ? 1 : 0;
                    result.CacheBackedTextureUpks += cacheBackedTextures > 0 ? 1 : 0;
                    result.AnimSequenceTotal += animSequenceTotal;
                    result.SkeletalBoneTotal += skeletalBoneTotal;
                }
            }

            record.Insights = insights;
            record.Tags = record.Tags.Concat(new[] { "Parsed", "UPK" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            record.Classification = "UPK / Unread";
            record.Summary = "Header parse failed.";
            record.Details = ex.Message;
            record.Insights = [new OmegaIntelInsight { Kind = "Error", Value = "UPK parse failed", Details = ex.Message }];
            log($"UPK parse failed: {Path.GetFileName(path)} -> {ex.Message}");
        }
    }

    private static int CountExports(IEnumerable<UnrealExportTableEntry> exports, string className)
    {
        return exports.Count(export => string.Equals(export.ClassReferenceNameIndex?.Name, className, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountExportsMatching(IEnumerable<UnrealExportTableEntry> exports, Func<UnrealExportTableEntry, bool> predicate)
    {
        return exports.Count(predicate);
    }

    private static bool ExportMatches(UnrealExportTableEntry export, params string[] names)
    {
        string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
        string objectName = export.ObjectNameIndex?.Name ?? string.Empty;

        foreach (string name in names)
        {
            if (string.Equals(className, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameContains(UnrealExportTableEntry export, string value)
    {
        string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
        string objectName = export.ObjectNameIndex?.Name ?? string.Empty;
        string path = export.GetPathName();

        return className.Contains(value, StringComparison.OrdinalIgnoreCase) ||
               objectName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
               path.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInterestingContentExport(UnrealExportTableEntry export)
    {
        string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
        return string.Equals(className, "Texture2D", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "SkeletalMesh", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "AnimSet", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "MarvelEntity", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("UI", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Anim", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Mesh", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Texture", StringComparison.OrdinalIgnoreCase);
    }

    private static void AnalyzeTfc(OmegaIntelFileRecord record, OmegaIntelScanResult? result, Action<string> log)
    {
        record.Classification = "TFC / Texture Cache";
        record.Summary = "Texture file cache container.";
        record.Details = "Read-only cache entry.";
        record.Insights = [new OmegaIntelInsight { Kind = "Cache", Value = "Texture cache", Details = "Potentially referenced by UPK texture exports." }];
        if (string.Equals(Path.GetFileName(record.Path), TextureManifest.ManifestName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                TextureManifest.Initialize();
                int entries = TextureManifest.Instance.LoadManifest(record.Path);
                record.Summary = $"Texture manifest loaded with {entries:N0} entries.";
                record.Details = $"Manifest path: {TextureManifest.Instance.ManifestPath}";
                if (result is not null)
                {
                    result.TfcManifestFiles++;
                    result.TfcEntriesTotal += entries;
                }
                record.Insights = record.Insights.Concat(new[]
                {
                    new OmegaIntelInsight { Kind = "Manifest", Value = "TextureFileCacheManifest.bin", Details = $"Entries={entries:N0}" }
                }).ToArray();
                log($"TFC manifest loaded: {Path.GetFileName(record.Path)} ({entries:N0} entries)");
            }
            catch (Exception ex)
            {
                record.Insights = record.Insights.Concat(new[]
                {
                    new OmegaIntelInsight { Kind = "ManifestError", Value = "Load failed", Details = ex.Message }
                }).ToArray();
                record.Details = ex.Message;
                log($"TFC manifest load failed: {Path.GetFileName(record.Path)} -> {ex.Message}");
            }
        }
    }

    private static void AnalyzeTexture(OmegaIntelFileRecord record)
    {
        record.Classification = "Texture / Disk Asset";
        record.Summary = "Image or texture asset on disk.";
        record.Details = "Non-UPK texture file.";
        record.Insights = [new OmegaIntelInsight { Kind = "Texture", Value = record.Extension.ToUpperInvariant(), Details = "Disk texture asset." }];
    }

    private static void AnalyzeMesh(OmegaIntelFileRecord record)
    {
        record.Classification = "Mesh / Disk Asset";
        record.Summary = "Mesh asset on disk.";
        record.Details = "Geometry import candidate.";
        record.Insights = [new OmegaIntelInsight { Kind = "Mesh", Value = record.Extension.ToUpperInvariant(), Details = "Disk mesh asset." }];
    }

    private static void AnalyzeAnimation(OmegaIntelFileRecord record)
    {
        record.Classification = "Animation / Disk Asset";
        record.Summary = "Animation asset on disk.";
        record.Details = "Potential motion or sequence source.";
        record.Insights = [new OmegaIntelInsight { Kind = "Animation", Value = record.Extension.ToUpperInvariant(), Details = "Disk animation asset." }];
    }

    private static void AnalyzeCharacter(OmegaIntelFileRecord record)
    {
        record.Classification = "Character / Definition";
        record.Summary = "Character definition or configuration file.";
        record.Details = "Likely character-specific data.";
        record.Insights = [new OmegaIntelInsight { Kind = "Character", Value = record.Extension.ToUpperInvariant(), Details = "Character-related file." }];
    }

    private static void AnalyzeUi(string path, OmegaIntelFileRecord record, OmegaIntelScanResult? result, Action<string> log)
    {
        record.Classification = "UI / Presentation";
        record.Summary = "UI or presentation content.";
        record.Details = "Interface-related resource.";
        record.Insights = [new OmegaIntelInsight { Kind = "UI", Value = record.Extension.ToUpperInvariant(), Details = "UI-related file." }];

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text = Encoding.Latin1.GetString(bytes);
            string lower = text.ToLowerInvariant();
            string? heroId = ExtractValue(text, "heroid", "heroid", "hero_id");
            string? portrait = ExtractValue(text, "portrait", "portraitref", "portraitreference", "portrait_path");
            string? layout = ExtractValue(text, "layout", "screen", "scene", "template");
            string? displayName = ExtractValue(text, "displayname", "display_name", "name");
            bool keywordMatch = UiHeroKeywords.Any(keyword => lower.Contains(keyword, StringComparison.OrdinalIgnoreCase) || record.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (keywordMatch)
            {
                string inferredName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : Path.GetFileNameWithoutExtension(path);

                if (result is not null)
                {
                    result.UiHeroEntriesList.Add(new OmegaIntelUiHeroEntry
                    {
                        Path = path,
                        DisplayName = inferredName,
                        Portrait = portrait,
                        Layout = layout,
                        HeroId = heroId
                    });
                    result.UiHeroEntries++;
                }

                string[] evidence = UiHeroKeywords.Where(keyword => lower.Contains(keyword, StringComparison.OrdinalIgnoreCase) || record.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase)).Take(6).ToArray();
                record.Summary = $"UI hero entry candidate: {inferredName}";
                record.Details = $"HeroId={heroId ?? "(none)"}, Portrait={portrait ?? "(none)"}, Layout={layout ?? "(none)"}, Hits={string.Join(", ", evidence)}";
                record.Insights = record.Insights.Concat(new[]
                {
                    new OmegaIntelInsight { Kind = "UIHero", Value = inferredName, Details = record.Details }
                }).ToArray();
            }
        }
        catch (Exception ex)
        {
            log($"UI analyze failed: {Path.GetFileName(path)} -> {ex.Message}");
        }
    }

    private static void AnalyzeExecutable(string path, OmegaIntelFileRecord record, OmegaIntelScanResult? result)
    {
        byte[] data = File.ReadAllBytes(path);
        bool isPe = data.Length >= 2 && data[0] == (byte)'M' && data[1] == (byte)'Z';
        string text = Encoding.Latin1.GetString(data);

        List<OmegaIntelInsight> insights = [];
        if (isPe && result is not null)
        {
            var sections = ParsePeSections(data, path, result);
            if (sections.Count > 0)
            {
                insights.Add(new OmegaIntelInsight
                {
                    Kind = "PE",
                    Value = $"{sections.Count:N0} section(s)",
                    Details = $"Sections: {string.Join(", ", sections.Select(section => section.SectionName))}"
                });
            }
        }

        foreach (string pattern in ExecutablePatterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                result?.ExePatternEntries.Add(new OmegaIntelExePatternEntry
                {
                    Path = path,
                    Pattern = pattern,
                    Evidence = "string-match"
                });
                if (result is not null)
                    result.ExePatternHits++;
                insights.Add(new OmegaIntelInsight
                {
                    Kind = "Pattern",
                    Value = pattern,
                    Details = "Read-only string pattern matched in executable."
                });
            }
        }

        foreach (Match match in HeroIdRegex.Matches(text))
        {
            string candidate = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            result?.HeroIdCandidatesList.Add(new OmegaIntelHeroIdCandidate
            {
                Path = path,
                Candidate = candidate,
                Evidence = match.Value
            });
            if (result is not null)
                result.HeroIdCandidates++;
        }

        foreach (Match match in RosterRegex.Matches(text))
        {
            string candidate = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (result is not null)
            {
                result.RosterTableCandidatesList.Add(new OmegaIntelRosterTableCandidate
                {
                    Path = path,
                    Candidate = candidate,
                    Evidence = match.Value
                });
                result.RosterTableCandidates++;
            }
        }

        foreach (Match match in PowerTreeRegex.Matches(text))
        {
            string candidate = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (result is not null)
            {
                result.PowerTreeCandidatesList.Add(new OmegaIntelPowerTreeCandidate
                {
                    Path = path,
                    Candidate = candidate,
                    Evidence = match.Value
                });
                result.PowerTreeCandidates++;
            }
        }

        foreach (Match match in PrintableStringRegex.Matches(text))
        {
            string value = match.Value.Trim();
            if (!ShouldCaptureStringTableValue(value))
                continue;

            if (result is not null)
            {
                result.StringTableEntriesList.Add(new OmegaIntelStringTableEntry
                {
                    Path = path,
                    Value = value,
                    Offset = match.Index,
                    Category = ClassifyStringValue(value)
                });
                result.StringTableEntries++;
            }
        }

        foreach (string signature in FunctionSignaturePatterns)
        {
            if (!text.Contains(signature, StringComparison.OrdinalIgnoreCase))
                continue;

            if (result is not null)
            {
                result.FunctionSignatureEntriesList.Add(new OmegaIntelFunctionSignatureEntry
                {
                    Path = path,
                    Signature = signature,
                    Evidence = "string-match"
                });
                result.FunctionSignatureEntries++;
            }
        }

        foreach (Match match in Regex.Matches(text, @"\b[A-Za-z0-9_]{6,}\b"))
        {
            string token = match.Value;
            if (!token.Contains("hero", StringComparison.OrdinalIgnoreCase) &&
                !token.Contains("roster", StringComparison.OrdinalIgnoreCase) &&
                !token.Contains("power", StringComparison.OrdinalIgnoreCase) &&
                !token.Contains("avatar", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (token.Length < 6)
                continue;

            if (result is not null)
            {
                result.HeroIdCandidatesList.Add(new OmegaIntelHeroIdCandidate
                {
                    Path = path,
                    Candidate = token,
                    Evidence = "token-scan"
                });
                result.HeroIdCandidates++;
            }
        }

        record.Classification = isPe ? "Executable / PE" : "Executable / Binary";
        record.Summary = insights.Count == 0
            ? "No string patterns matched."
            : $"{insights.Count:N0} pattern(s) matched.";
        record.Details = isPe ? "PE header detected." : "PE header not detected.";
        if (result is not null)
        {
            record.Details += $" Patterns={result.ExePatternHits:N0}, HeroIds={result.HeroIdCandidates:N0}, Roster={result.RosterTableCandidates:N0}, Power={result.PowerTreeCandidates:N0}, Strings={result.StringTableEntries:N0}, Signatures={result.FunctionSignatureEntries:N0}, Sections={result.ExeSectionEntries:N0}";
        }
        record.Insights = insights;
    }

    private static List<OmegaIntelExeSectionEntry> ParsePeSections(byte[] data, string path, OmegaIntelScanResult result)
    {
        List<OmegaIntelExeSectionEntry> sections = [];
        if (data.Length < 0x40)
            return sections;

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x3C, 4));
        if (peOffset < 0 || peOffset + 24 > data.Length)
            return sections;

        if (data[peOffset] != (byte)'P' || data[peOffset + 1] != (byte)'E')
            return sections;

        ushort numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(peOffset + 6, 2));
        ushort sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(peOffset + 20, 2));
        int sectionOffset = peOffset + 24 + sizeOfOptionalHeader;
        int sectionSize = 40;

        for (int i = 0; i < numberOfSections; i++)
        {
            int offset = sectionOffset + i * sectionSize;
            if (offset + sectionSize > data.Length)
                break;

            string rawName = Encoding.ASCII.GetString(data, offset, 8).TrimEnd('\0');
            long virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8, 4));
            long virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 12, 4));
            long rawSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16, 4));
            byte[] slice = data.Skip(offset + sectionSize).Take((int)Math.Min(rawSize, Math.Max(0, data.Length - (offset + sectionSize)))).ToArray();

            OmegaIntelExeSectionEntry entry = new()
            {
                Path = path,
                SectionName = string.IsNullOrWhiteSpace(rawName) ? $".sec{i}" : rawName,
                VirtualSize = virtualSize,
                RawSize = rawSize,
                VirtualAddress = virtualAddress,
                Entropy = CalculateEntropy(slice)
            };
            sections.Add(entry);
            result.ExeSectionEntriesList.Add(entry);
            result.ExeSectionEntries++;
        }

        return sections;
    }

    private static bool ShouldCaptureStringTableValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4 || value.Length > 120)
            return false;

        return StringTableKeywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               value.Contains("\\", StringComparison.Ordinal) ||
               value.Contains("/", StringComparison.Ordinal) ||
               value.Contains(":", StringComparison.Ordinal) ||
               value.StartsWith("UI", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("Hero", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ClassifyStringValue(string value)
    {
        if (value.Contains("hero", StringComparison.OrdinalIgnoreCase))
            return "hero";
        if (value.Contains("roster", StringComparison.OrdinalIgnoreCase))
            return "roster";
        if (value.Contains("power", StringComparison.OrdinalIgnoreCase))
            return "power";
        if (value.Contains("ui", StringComparison.OrdinalIgnoreCase))
            return "ui";
        if (value.Contains("texture", StringComparison.OrdinalIgnoreCase))
            return "texture";
        if (value.Contains("mesh", StringComparison.OrdinalIgnoreCase))
            return "mesh";
        if (value.Contains("anim", StringComparison.OrdinalIgnoreCase))
            return "anim";
        if (value.Contains("cache", StringComparison.OrdinalIgnoreCase))
            return "cache";
        if (value.Contains("package", StringComparison.OrdinalIgnoreCase))
            return "package";
        return null;
    }

    private static string? ExtractValue(string text, params string[] keys)
    {
        foreach (string key in keys)
        {
            var match = Regex.Match(text, $@"(?im)^\s*{Regex.Escape(key)}\s*[:=]\s*(.+)$");
            if (match.Success)
            {
                string value = match.Groups[1].Value.Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static async Task<byte[]> ReadSampleAsync(string path, int maxBytes = 4096)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int length = (int)Math.Min(maxBytes, stream.Length);
            byte[] buffer = new byte[length];
            int read = await stream.ReadAsync(buffer, 0, length).ConfigureAwait(false);
            if (read <= 0)
                return [];

            if (read == buffer.Length)
                return buffer;

            byte[] exact = new byte[read];
            Buffer.BlockCopy(buffer, 0, exact, 0, read);
            return exact;
        }
        catch
        {
            return [];
        }
    }

    private static string FormatMagicBytes(byte[] bytes, int count = 4)
    {
        if (bytes.Length == 0)
            return string.Empty;

        int take = Math.Min(count, bytes.Length);
        return string.Join(" ", bytes.Take(take).Select(value => value.ToString("X2")));
    }

    private static double CalculateEntropy(byte[] bytes)
    {
        if (bytes.Length == 0)
            return 0.0;

        Span<int> counts = stackalloc int[256];
        foreach (byte value in bytes)
            counts[value]++;

        double length = bytes.Length;
        double entropy = 0.0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
                continue;

            double probability = counts[i] / length;
            entropy -= probability * Math.Log(probability, 2);
        }

        return entropy;
    }

    private static void BuildDirectoryRecords(OmegaIntelScanResult result)
    {
        var directories = result.Files
            .GroupBy(file => file.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OmegaIntelDirectoryRecord
            {
                Path = group.Key,
                FileCount = group.Count(),
                TotalBytes = group.Sum(item => item.SizeBytes),
                UpkCount = group.Count(item => item.Kind == OmegaIntelFileKind.Upk),
                TfcCount = group.Count(item => item.Kind == OmegaIntelFileKind.Tfc),
                TextureCount = group.Count(item => item.Kind == OmegaIntelFileKind.Texture),
                MeshCount = group.Count(item => item.Kind == OmegaIntelFileKind.Mesh),
                AnimationCount = group.Count(item => item.Kind == OmegaIntelFileKind.Animation)
            })
            .OrderByDescending(item => item.FileCount)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.Directories = directories;
        result.DirectoryCount = directories.Count;
    }

    private static void RunValidation(OmegaIntelScanResult result, Action<string> log)
    {
        result.ValidationIssuesList.Clear();

        void AddIssue(string severity, string code, string message, string? details = null, string? path = null)
        {
            result.ValidationIssuesList.Add(new OmegaIntelValidationIssue
            {
                Severity = severity,
                Code = code,
                Message = message,
                Details = details,
                Path = path
            });
        }

        if (result.TotalFiles <= 0)
            AddIssue("Error", "EMPTY_SCAN", "No files were discovered in the selected root folder.");

        if (result.TotalFiles > 0 && result.ClassifiedFiles < Math.Max(1, (int)(result.TotalFiles * 0.75)))
        {
            AddIssue("Warning", "LOW_CLASSIFY_COVERAGE", "A large portion of files were not classified.", $"Classified {result.ClassifiedFiles:N0} of {result.TotalFiles:N0} files.");
        }

        if (result.TfcFiles > 0 && (TextureManifest.Instance == null || TextureManifest.Instance.Entries.Count == 0))
            AddIssue("Warning", "MISSING_MANIFEST", "Texture cache files were found without a loaded manifest.", "Texture resolution may be incomplete.");

        if (result.UpkFiles > 0 && result.ResolvedTextureUpks == 0 && result.ResolvedMeshUpks == 0 && result.ResolvedAnimSetUpks == 0 && result.ResolvedCharacterUpks == 0)
            AddIssue("Warning", "UNRESOLVED_UPKS", "UPK files were found but no resolved exports were captured.", "Consider enabling deep UPK analysis and checking parse coverage.");

        if (result.ExecutableFiles > 0 && result.ExeSectionEntries == 0)
            AddIssue("Info", "PE_SECTIONS_NONE", "Executable files were found but no PE sections were parsed.", "The files may be stripped, invalid, or non-PE binaries.");

        if (result.HighEntropyFiles > 0 && result.HighEntropyFiles >= Math.Max(5, result.TotalFiles / 2))
            AddIssue("Info", "HIGH_ENTROPY", "Many files appear high entropy.", "This can be normal for binaries, caches, and packed assets.");

        if (result.UnknownMagicFiles > 0 && result.UnknownMagicFiles >= Math.Max(5, result.TotalFiles / 2))
            AddIssue("Info", "UNKNOWN_MAGIC", "Many files have unknown or zeroed magic bytes.", "This usually means mixed binary content or truncated samples.");

        result.ValidationWarnings = result.ValidationIssuesList.Count(item => string.Equals(item.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        result.ValidationErrors = result.ValidationIssuesList.Count(item => string.Equals(item.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        result.ValidationIssues = result.ValidationIssuesList.Count;

        if (result.ValidationIssuesList.Count > 0)
        {
            foreach (var issue in result.ValidationIssuesList)
                log($"Validation {issue.Severity}: {issue.Code} - {issue.Message}");

            result.Notes = string.Join(Environment.NewLine, result.ValidationIssuesList.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}"));
        }
    }

    private static string? FindManifestPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        string direct = Path.Combine(directory, TextureManifest.ManifestName);
        if (File.Exists(direct))
            return direct;

        string? parent = Directory.GetParent(directory)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            string parentPath = Path.Combine(parent, TextureManifest.ManifestName);
            if (File.Exists(parentPath))
                return parentPath;
        }

        return null;
    }

    private static void TryAutoLoadTextureManifest(string rootPath, OmegaIntelScanResult result, Action<string> log)
    {
        try
        {
            TextureManifest.Initialize();
            string? manifestPath = Directory.EnumerateFiles(rootPath, TextureManifest.ManifestName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                log($"Texture manifest not found under {rootPath}.");
                return;
            }

            int entries = TextureManifest.Instance.LoadManifest(manifestPath);
            result.TfcManifestFiles = Math.Max(result.TfcManifestFiles, 1);
            result.TfcEntriesTotal += entries;
            log($"Auto-loaded texture manifest: {manifestPath} ({entries:N0} entries)");
        }
        catch (Exception ex)
        {
            log($"Auto-load texture manifest failed: {ex.Message}");
        }
    }

    private static void AnalyzeConfig(OmegaIntelFileRecord record)
    {
        record.Classification = "Config";
        record.Summary = "Configuration or INI-style data.";
        record.Details = "Potential runtime or tuning config.";
        record.Insights = [new OmegaIntelInsight { Kind = "Config", Value = record.Extension.ToUpperInvariant(), Details = "Configuration file." }];
    }

    private static void AnalyzeScript(OmegaIntelFileRecord record)
    {
        record.Classification = "Script";
        record.Summary = "Script or source file.";
        record.Details = "Likely UnrealScript or similar.";
        record.Insights = [new OmegaIntelInsight { Kind = "Script", Value = record.Extension.ToUpperInvariant(), Details = "Script-related file." }];
    }

    private static void AnalyzeLibrary(OmegaIntelFileRecord record)
    {
        record.Classification = "Library";
        record.Summary = "Shared binary library.";
        record.Details = "Loaded by tools or game runtime.";
        record.Insights = [new OmegaIntelInsight { Kind = "Library", Value = record.Extension.ToUpperInvariant(), Details = "Binary library." }];
    }

    private static void AnalyzeUnknown(OmegaIntelFileRecord record)
    {
        record.Classification = "Unknown";
        record.Summary = "File type not specifically classified.";
        record.Details = "No rule matched.";
        record.Insights = [];
    }

    private static OmegaIntelFileKind ClassifyByExtension(string extension)
    {
        return extension switch
        {
            ".upk" => OmegaIntelFileKind.Upk,
            ".tfc" => OmegaIntelFileKind.Tfc,
            ".dds" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" => OmegaIntelFileKind.Texture,
            ".fbx" or ".psk" or ".psa" or ".obj" => OmegaIntelFileKind.Mesh,
            ".anim" or ".seq" => OmegaIntelFileKind.Animation,
            ".ini" or ".int" or ".cfg" or ".xml" or ".json" or ".tsv" => OmegaIntelFileKind.Config,
            ".uc" => OmegaIntelFileKind.Script,
            ".dll" => OmegaIntelFileKind.Library,
            ".exe" => OmegaIntelFileKind.Executable,
            _ => OmegaIntelFileKind.Data
        };
    }

    private static IEnumerable<string> BuildTags(OmegaIntelFileKind kind, string extension)
    {
        yield return kind.ToString();
        if (!string.IsNullOrWhiteSpace(extension))
            yield return extension.TrimStart('.').ToUpperInvariant();
    }

    private static string BuildSummary(OmegaIntelScanResult result)
    {
        TimeSpan elapsed = result.FinishedUtc > result.StartedUtc ? result.FinishedUtc - result.StartedUtc : TimeSpan.Zero;
        string validation = result.ValidationErrors > 0
            ? $"{result.ValidationErrors:N0} error(s), {result.ValidationWarnings:N0} warning(s)"
            : result.ValidationWarnings > 0
                ? $"{result.ValidationWarnings:N0} warning(s)"
                : "clean";

        return $"Scanned {result.TotalFiles:N0} files in {result.DirectoryCount:N0} directories in {elapsed.TotalSeconds:0.0}s; {result.UpkFiles:N0} UPKs, {result.TfcFiles:N0} TFCs, {result.TextureFiles:N0} textures, {result.MeshFiles:N0} meshes, {result.AnimationFiles:N0} animations, {result.ResolvedTextureUpks:N0} texture UPKs resolved, {result.ResolvedAnimSetUpks:N0} anim-set UPKs resolved; validation {validation}.";
    }
}

