using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

internal sealed class OmegaIntelReportService
{
    private readonly UpkFileRepository upkRepository = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task ExportAllAsync(OmegaIntelScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(OmegaIntelPaths.ReportsDirectory);
        Directory.CreateDirectory(OmegaIntelPaths.CacheDirectory);

        string stamp = result.ReportStamp;
        await File.WriteAllTextAsync(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{stamp}.json"), JsonSerializer.Serialize(result, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{stamp}.csv"), BuildCsv(result));
        await File.WriteAllTextAsync(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{stamp}.md"), BuildMarkdown(result));
        await File.WriteAllTextAsync(Path.Combine(OmegaIntelPaths.ReportsDirectory, $"OmegaIntel_{stamp}.html"), BuildHtml(result));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("files.json"), JsonSerializer.Serialize(result.Files, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("file_types.json"), JsonSerializer.Serialize(result.Files.GroupBy(item => item.Kind).Select(group => new { Kind = group.Key, Count = group.Count(), Files = group.Select(file => file.Path).ToArray() }), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("unknown_files.json"), JsonSerializer.Serialize(result.Files.Where(item => item.Kind == OmegaIntelFileKind.Unknown).ToArray(), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("entropy_map.json"), JsonSerializer.Serialize(result.Files.Select(item => new { item.Path, item.MagicBytes, item.Entropy, item.SizeBytes }).OrderByDescending(item => item.Entropy), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("directory_map.json"), JsonSerializer.Serialize(result.Directories, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("tfc_map.json"), JsonSerializer.Serialize(result.TfcMap, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("ui_hero_select.json"), JsonSerializer.Serialize(result.UiHeroEntriesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("exe_patterns.json"), JsonSerializer.Serialize(result.ExePatternEntries, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("exe_sections.json"), JsonSerializer.Serialize(result.ExeSectionEntriesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("hero_id_candidates.json"), JsonSerializer.Serialize(result.HeroIdCandidatesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("roster_table_candidates.json"), JsonSerializer.Serialize(result.RosterTableCandidatesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("power_tree_candidates.json"), JsonSerializer.Serialize(result.PowerTreeCandidatesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("string_table.json"), JsonSerializer.Serialize(result.StringTableEntriesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("function_signatures.json"), JsonSerializer.Serialize(result.FunctionSignatureEntriesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("validation.json"), JsonSerializer.Serialize(result.ValidationIssuesList, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("upk_summary.json"), JsonSerializer.Serialize(BuildUpkSummary(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("exports_by_class.json"), JsonSerializer.Serialize(BuildExportsByClass(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("upk_cross_refs.json"), JsonSerializer.Serialize(BuildUpkCrossRefs(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("textures_index.json"), JsonSerializer.Serialize(BuildTexturesIndex(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("skeletons.json"), JsonSerializer.Serialize(BuildSkeletons(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("mesh_skeleton_map.json"), JsonSerializer.Serialize(BuildMeshSkeletonMap(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("animations.json"), JsonSerializer.Serialize(BuildAnimations(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("anim_to_skeleton.json"), JsonSerializer.Serialize(BuildAnimToSkeletonMap(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("heroes.json"), JsonSerializer.Serialize(BuildHeroes(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("hero_asset_map.json"), JsonSerializer.Serialize(BuildHeroAssetMap(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("xref_graph.json"), JsonSerializer.Serialize(new
        {
            result.RootPath,
            result.Nodes,
            result.Edges,
            GeneratedUtc = DateTime.UtcNow
        }, JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("asset_dependency_tree.json"), JsonSerializer.Serialize(BuildAssetDependencyTree(result), JsonOptions));
        await File.WriteAllTextAsync(OmegaIntelPaths.GetReportPath("reverse_lookup.json"), JsonSerializer.Serialize(BuildReverseLookup(result), JsonOptions));

        await File.WriteAllTextAsync(OmegaIntelPaths.LatestScanCachePath, JsonSerializer.Serialize(new OmegaIntelReportSnapshot
        {
            RootPath = result.RootPath,
            GeneratedUtc = DateTime.UtcNow,
            Summary = result.Summary ?? string.Empty,
            Files = result.Files,
            Nodes = result.Nodes,
            Edges = result.Edges
        }, JsonOptions));

        await File.WriteAllTextAsync(OmegaIntelPaths.LatestGraphCachePath, JsonSerializer.Serialize(new
        {
            result.RootPath,
            result.Nodes,
            result.Edges,
            GeneratedUtc = DateTime.UtcNow
        }, JsonOptions));
    }

    public async Task<string> ExportMigrationReadinessAsync(OmegaIntelScanResult result, string sourceUpkPath, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        string fullSourcePath = Path.GetFullPath(sourceUpkPath);
        string directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? OmegaIntelPaths.MigrationReadinessDirectory
            : Path.GetFullPath(outputDirectory);

        Directory.CreateDirectory(directory);

        var header = await LoadUpkHeaderAsync(fullSourcePath).ConfigureAwait(false);
        var report = BuildMigrationReadinessReport(result, fullSourcePath, header, directory);
        string stamp = result.ReportStamp;

        string jsonPath = Path.Combine(directory, $"OmegaIntel_MigrationReadiness_{stamp}.json");
        string mdPath = Path.Combine(directory, $"OmegaIntel_MigrationReadiness_{stamp}.md");
        string htmlPath = Path.Combine(directory, $"OmegaIntel_MigrationReadiness_{stamp}.html");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(mdPath, BuildMigrationReadinessMarkdown(report)).ConfigureAwait(false);
        await File.WriteAllTextAsync(htmlPath, BuildMigrationReadinessHtml(report)).ConfigureAwait(false);
        await File.WriteAllTextAsync(OmegaIntelPaths.LatestMigrationReadinessPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);

        return directory;
    }

    private static string BuildCsv(OmegaIntelScanResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("Path,Directory,Kind,Classification,SizeBytes,LastWriteTimeUtc,MagicBytes,Entropy,Summary");

        foreach (var item in result.Files)
        {
            builder.AppendLine(string.Join(",",
                Csv(item.Path),
                Csv(item.Directory),
                Csv(item.Kind.ToString()),
                Csv(item.Classification),
                item.SizeBytes.ToString(CultureInfo.InvariantCulture),
                Csv(item.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)),
                Csv(item.MagicBytes),
                item.Entropy.ToString("F2", CultureInfo.InvariantCulture),
                Csv(item.Summary)));
        }

        return builder.ToString();
    }

    private static string BuildMarkdown(OmegaIntelScanResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Omega Intelligence Engine");
        builder.AppendLine();
        builder.AppendLine($"- Root: `{result.RootPath}`");
        builder.AppendLine($"- Files: {result.TotalFiles:N0}");
        builder.AppendLine($"- Classified: {result.ClassifiedFiles:N0}");
        builder.AppendLine($"- UPKs: {result.UpkFiles:N0}");
        builder.AppendLine($"- TFCs: {result.TfcFiles:N0}");
        builder.AppendLine($"- TFC manifests: {result.TfcManifestFiles:N0}");
        builder.AppendLine($"- Textures: {result.TextureFiles:N0}");
        builder.AppendLine($"- Meshes: {result.MeshFiles:N0}");
        builder.AppendLine($"- Animations: {result.AnimationFiles:N0}");
        builder.AppendLine($"- Executable PE sections: {result.ExeSectionEntries:N0}");
        builder.AppendLine($"- Directories: {result.DirectoryCount:N0}");
        builder.AppendLine($"- High entropy files: {result.HighEntropyFiles:N0}");
        builder.AppendLine($"- UI hero entries: {result.UiHeroEntries:N0}");
        builder.AppendLine($"- EXE pattern hits: {result.ExePatternHits:N0}");
        builder.AppendLine($"- Hero ID candidates: {result.HeroIdCandidates:N0}");
        builder.AppendLine($"- Roster table candidates: {result.RosterTableCandidates:N0}");
        builder.AppendLine($"- Power tree candidates: {result.PowerTreeCandidates:N0}");
        builder.AppendLine($"- String table entries: {result.StringTableEntries:N0}");
        builder.AppendLine($"- Function signature entries: {result.FunctionSignatureEntries:N0}");
        builder.AppendLine($"- Executables: {result.ExecutableFiles:N0}");
        builder.AppendLine($"- Validation: {result.ValidationIssues:N0} issue(s) ({result.ValidationErrors:N0} error(s), {result.ValidationWarnings:N0} warning(s))");
        builder.AppendLine($"- Resolved skeletal meshes: {result.ResolvedSkeletalMeshUpks:N0}");
        builder.AppendLine($"- Resolved static meshes: {result.ResolvedStaticMeshUpks:N0}");
        if (result.ValidationIssuesList.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Validation");
            foreach (var issue in result.ValidationIssuesList)
                builder.AppendLine($"- [{issue.Severity}] {issue.Code}: {issue.Message}");
        }
        builder.AppendLine();
        builder.AppendLine("## Analysis Notes");
        builder.AppendLine("- Read-only scan of file names, headers, exports, and string patterns.");
        builder.AppendLine("- PE executables include a structural section-table pass.");
        builder.AppendLine("- UPK/TFC analysis is metadata-driven and does not modify source files.");
        builder.AppendLine();
        builder.AppendLine("| File | Directory | Kind | Classification | Summary |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var item in result.Files)
        {
            builder.AppendLine($"| {Md(item.Name)} | {Md(item.Directory)} | {Md(item.Kind.ToString())} | {Md(item.Classification)} | {Md(item.Summary)} |");
        }

        return builder.ToString();
    }

    private static string BuildHtml(OmegaIntelScanResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Omega Intelligence Engine</title>");
        builder.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#101114;color:#e7e7e7;padding:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #333;padding:8px;text-align:left;vertical-align:top}th{background:#1b1f27}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine("<h1>Omega Intelligence Engine</h1>");
        builder.AppendLine($"<p><strong>Root:</strong> {Html(result.RootPath)}</p>");
        builder.AppendLine("<ul>");
        builder.AppendLine($"<li>Files: {result.TotalFiles:N0}</li>");
        builder.AppendLine($"<li>Classified: {result.ClassifiedFiles:N0}</li>");
        builder.AppendLine($"<li>UPKs: {result.UpkFiles:N0}</li>");
        builder.AppendLine($"<li>TFCs: {result.TfcFiles:N0}</li>");
        builder.AppendLine($"<li>TFC manifests: {result.TfcManifestFiles:N0}</li>");
        builder.AppendLine($"<li>Textures: {result.TextureFiles:N0}</li>");
        builder.AppendLine($"<li>Meshes: {result.MeshFiles:N0}</li>");
        builder.AppendLine($"<li>Animations: {result.AnimationFiles:N0}</li>");
        builder.AppendLine($"<li>Executable PE sections: {result.ExeSectionEntries:N0}</li>");
        builder.AppendLine($"<li>Directories: {result.DirectoryCount:N0}</li>");
        builder.AppendLine($"<li>High entropy files: {result.HighEntropyFiles:N0}</li>");
        builder.AppendLine($"<li>UI hero entries: {result.UiHeroEntries:N0}</li>");
        builder.AppendLine($"<li>EXE pattern hits: {result.ExePatternHits:N0}</li>");
        builder.AppendLine($"<li>Hero ID candidates: {result.HeroIdCandidates:N0}</li>");
        builder.AppendLine($"<li>Roster table candidates: {result.RosterTableCandidates:N0}</li>");
        builder.AppendLine($"<li>Power tree candidates: {result.PowerTreeCandidates:N0}</li>");
        builder.AppendLine($"<li>String table entries: {result.StringTableEntries:N0}</li>");
        builder.AppendLine($"<li>Function signature entries: {result.FunctionSignatureEntries:N0}</li>");
        builder.AppendLine($"<li>Executables: {result.ExecutableFiles:N0}</li>");
        builder.AppendLine($"<li>Validation: {result.ValidationIssues:N0} issue(s) ({result.ValidationErrors:N0} error(s), {result.ValidationWarnings:N0} warning(s))</li>");
        builder.AppendLine($"<li>Resolved skeletal meshes: {result.ResolvedSkeletalMeshUpks:N0}</li>");
        builder.AppendLine($"<li>Resolved static meshes: {result.ResolvedStaticMeshUpks:N0}</li>");
        builder.AppendLine("</ul>");
        if (result.ValidationIssuesList.Count > 0)
        {
            builder.AppendLine("<h2>Validation</h2><ul>");
            foreach (var issue in result.ValidationIssuesList)
                builder.AppendLine($"<li><strong>{Html(issue.Severity)}</strong> {Html(issue.Code)}: {Html(issue.Message)}</li>");
            builder.AppendLine("</ul>");
        }
        builder.AppendLine("<h2>Analysis Notes</h2><ul>");
        builder.AppendLine("<li>Read-only scan of file names, headers, exports, and string patterns.</li>");
        builder.AppendLine("<li>PE executables include a structural section-table pass.</li>");
        builder.AppendLine("<li>UPK/TFC analysis is metadata-driven and does not modify source files.</li>");
        builder.AppendLine("</ul>");
        builder.AppendLine("<table><thead><tr><th>File</th><th>Directory</th><th>Kind</th><th>Classification</th><th>Summary</th></tr></thead><tbody>");

        foreach (var item in result.Files)
        {
            builder.AppendLine($"<tr><td>{Html(item.Name)}</td><td>{Html(item.Directory)}</td><td>{Html(item.Kind.ToString())}</td><td>{Html(item.Classification)}</td><td>{Html(item.Summary)}</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private async Task<UnrealHeader?> LoadUpkHeaderAsync(string sourceUpkPath)
    {
        var file = await upkRepository.LoadUpkFile(sourceUpkPath).ConfigureAwait(false);
        await file.ReadHeaderAsync(null).ConfigureAwait(false);
        return file;
    }

    private static object BuildMigrationReadinessReport(OmegaIntelScanResult result, string sourceUpkPath, UnrealHeader? header, string outputDirectory)
    {
        var upkFiles = result.Files.Where(item => item.Kind == OmegaIntelFileKind.Upk).ToArray();
        var exportsByClass = upkFiles
            .SelectMany(item => item.Insights.Where(insight => string.Equals(insight.Kind, "ExportClass", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(insight => insight.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Class = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Class)
            .ToArray();

        var unresolvedIssues = result.ValidationIssuesList
            .Where(issue => issue.Severity.Contains("Warn", StringComparison.OrdinalIgnoreCase) ||
                            issue.Code.Contains("UNRESOLVED", StringComparison.OrdinalIgnoreCase) ||
                            issue.Message.Contains("resolve", StringComparison.OrdinalIgnoreCase) ||
                            issue.Message.Contains("range", StringComparison.OrdinalIgnoreCase))
            .Select(issue => new
            {
                issue.Severity,
                issue.Code,
                issue.Message,
                issue.Path,
                issue.Details
            })
            .ToArray();

        var dependencyNodes = result.Nodes
            .OrderByDescending(node => node.Weight)
            .ThenBy(node => node.Label)
            .Take(200)
            .Select(node => new
            {
                node.Id,
                node.Kind,
                node.Label,
                node.SourcePath,
                node.Description,
                node.Weight
            })
            .ToArray();

        var dependencyEdges = result.Edges
            .Select(edge => new
            {
                edge.FromId,
                edge.ToId,
                edge.Label
            })
            .ToArray();

        var exportRefHealth = header?.ExportTable
            .Select(export => new
            {
                Path = export.GetPathName(),
                Class = export.ClassReferenceNameIndex?.Name ?? export.ClassReferenceNameIndex?.ToString() ?? "Unknown",
                Outer = export.OuterReferenceNameIndex?.Name ?? export.OuterReferenceNameIndex?.ToString() ?? "Unresolved",
                Super = export.SuperReferenceNameIndex?.Name ?? export.SuperReferenceNameIndex?.ToString() ?? "Unresolved",
                Archetype = export.ArchetypeReferenceNameIndex?.Name ?? export.ArchetypeReferenceNameIndex?.ToString() ?? "Unresolved",
                HasSerialData = export.SerialDataSize > 0,
                ObjectFlags = export.ObjectFlags.ToString(),
                ExportFlags = export.ExportFlags.ToString()
            })
            .ToArray() ?? [];

        int score = 100;
        score -= result.ValidationErrors * 15;
        score -= result.ValidationWarnings * 4;
        score -= unresolvedIssues.Length * 2;
        score -= Math.Max(0, result.UpkFiles - result.ResolvedSkeletalMeshUpks - result.ResolvedStaticMeshUpks - result.ResolvedTextureUpks - result.ResolvedAnimSetUpks) * 2;
        score = Math.Clamp(score, 0, 100);

        string readiness;
        if (score >= 85)
            readiness = "Green";
        else if (score >= 60)
            readiness = "Yellow";
        else
            readiness = "Red";

        var tags = new[]
        {
            result.TextureLikeUpks > 0 ? "TextureHeavy" : null,
            result.MeshLikeUpks > 0 ? "MeshHeavy" : null,
            result.AnimationLikeUpks > 0 ? "AnimationHeavy" : null,
            result.UiLikeUpks > 0 ? "UIHeavy" : null,
            result.CharacterLikeUpks > 0 ? "CharacterHeavy" : null,
            result.ResolvedTextureUpks > 0 ? "ResolvedTexture" : null,
            result.ResolvedMeshUpks > 0 ? "ResolvedMesh" : null,
            result.ResolvedAnimSetUpks > 0 ? "ResolvedAnimSet" : null,
            result.ResolvedCharacterUpks > 0 ? "ResolvedCharacter" : null,
            result.ValidationErrors > 0 ? "HasErrors" : null,
            result.ValidationWarnings > 0 ? "HasWarnings" : null
        }.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray();

        return new
        {
            SourceUpkPath = sourceUpkPath,
            OutputDirectory = outputDirectory,
            GeneratedUtc = DateTime.UtcNow,
            Package = new
            {
                header?.Version,
                header?.Licensee,
                header?.Flags,
                header?.NameTableCount,
                header?.ExportTableCount,
                header?.ImportTableCount,
                header?.DependsTableOffset,
                header?.NameTableOffset,
                header?.ExportTableOffset,
                header?.ImportTableOffset,
                header?.CompressionFlags,
                header?.CompressionTableCount,
                header?.PackageSource,
                header?.EngineVersion,
                header?.CookerVersion,
                header?.Guid
            },
            Summary = result.Summary,
            Counts = new
            {
                result.TotalFiles,
                result.DirectoryCount,
                result.UpkFiles,
                result.TfcFiles,
                result.TextureFiles,
                result.MeshFiles,
                result.AnimationFiles,
                result.CharacterFiles,
                result.UiFiles,
                result.ResolvedTextureUpks,
                result.ResolvedMeshUpks,
                result.ResolvedAnimSetUpks,
                result.ResolvedCharacterUpks,
                result.ValidationIssues,
                result.ValidationWarnings,
                result.ValidationErrors
            },
            ExportClassBreakdown = exportsByClass,
            ExportReferenceHealth = exportRefHealth,
            UnresolvedIssues = unresolvedIssues,
            DependencyGraph = new
            {
                Nodes = dependencyNodes,
                Edges = dependencyEdges
            },
            Tags = tags,
            ReadinessScore = score,
            ReadinessLevel = readiness,
            AssetInventory = new
            {
                result.TextureLikeUpks,
                result.MeshLikeUpks,
                result.AnimationLikeUpks,
                result.UiLikeUpks,
                result.CharacterLikeUpks,
                result.CacheBackedTextureUpks,
                result.AnimSequenceTotal,
                result.SkeletalBoneTotal
            },
            Recommendations = BuildMigrationRecommendations(result, score)
        };
    }

    private static string[] BuildMigrationRecommendations(OmegaIntelScanResult result, int score)
    {
        List<string> recommendations = [];

        if (result.ValidationErrors > 0)
            recommendations.Add("Resolve validation errors before migration.");
        if (result.ValidationWarnings > 0)
            recommendations.Add("Review warnings for unresolved references and out-of-range indices.");
        if (result.ResolvedTextureUpks == 0 && result.TextureLikeUpks > 0)
            recommendations.Add("Verify texture cache and material references.");
        if (result.ResolvedMeshUpks == 0 && result.MeshLikeUpks > 0)
            recommendations.Add("Verify mesh export selection and section mapping.");
        if (result.ResolvedAnimSetUpks == 0 && result.AnimationLikeUpks > 0)
            recommendations.Add("Verify animset resolution and skeleton links.");
        if (score < 85)
            recommendations.Add("Use the export reference health table to prune or remap broken references before writing a clean 1.52 package.");

        return recommendations.ToArray();
    }

    private static string BuildMigrationReadinessMarkdown(object report)
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        string json = JsonSerializer.Serialize(report, options);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        StringBuilder builder = new();
        builder.AppendLine("# Omega Intel Migration Readiness");
        builder.AppendLine();
        AppendMarkdownValue(builder, "Source UPK", root, "SourceUpkPath");
        AppendMarkdownValue(builder, "Output Directory", root, "OutputDirectory");
        AppendMarkdownValue(builder, "Readiness Score", root, "ReadinessScore");
        AppendMarkdownValue(builder, "Readiness Level", root, "ReadinessLevel");
        builder.AppendLine();
        builder.AppendLine("## Package");
        AppendMarkdownObject(builder, root, "Package");
        builder.AppendLine();
        builder.AppendLine("## Counts");
        AppendMarkdownObject(builder, root, "Counts");
        builder.AppendLine();
        builder.AppendLine("## Export Class Breakdown");
        AppendMarkdownArray(builder, root, "ExportClassBreakdown");
        builder.AppendLine();
        builder.AppendLine("## Export Reference Health");
        AppendMarkdownArray(builder, root, "ExportReferenceHealth");
        builder.AppendLine();
        builder.AppendLine("## Unresolved Issues");
        AppendMarkdownArray(builder, root, "UnresolvedIssues");
        builder.AppendLine();
        builder.AppendLine("## Dependency Graph");
        AppendMarkdownObject(builder, root, "DependencyGraph");
        builder.AppendLine();
        builder.AppendLine("## Tags");
        AppendMarkdownArray(builder, root, "Tags");
        builder.AppendLine();
        builder.AppendLine("## Recommendations");
        AppendMarkdownArray(builder, root, "Recommendations");
        return builder.ToString();
    }

    private static string BuildMigrationReadinessHtml(object report)
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        string json = JsonSerializer.Serialize(report, options);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        StringBuilder builder = new();
        builder.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Omega Intel Migration Readiness</title>");
        builder.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#101114;color:#e7e7e7;padding:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #333;padding:8px;text-align:left;vertical-align:top}th{background:#1b1f27}code{background:#1b1f27;padding:2px 4px;border-radius:4px}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine("<h1>Omega Intel Migration Readiness</h1>");
        AppendHtmlParagraph(builder, "Source UPK", root, "SourceUpkPath");
        AppendHtmlParagraph(builder, "Output Directory", root, "OutputDirectory");
        AppendHtmlParagraph(builder, "Readiness Score", root, "ReadinessScore");
        AppendHtmlParagraph(builder, "Readiness Level", root, "ReadinessLevel");
        builder.AppendLine("<h2>Package</h2>");
        AppendHtmlDefinitionList(builder, root, "Package");
        builder.AppendLine("<h2>Counts</h2>");
        AppendHtmlDefinitionList(builder, root, "Counts");
        builder.AppendLine("<h2>Export Class Breakdown</h2>");
        AppendHtmlArray(builder, root, "ExportClassBreakdown");
        builder.AppendLine("<h2>Export Reference Health</h2>");
        AppendHtmlArray(builder, root, "ExportReferenceHealth");
        builder.AppendLine("<h2>Unresolved Issues</h2>");
        AppendHtmlArray(builder, root, "UnresolvedIssues");
        builder.AppendLine("<h2>Dependency Graph</h2>");
        AppendHtmlDefinitionList(builder, root, "DependencyGraph");
        builder.AppendLine("<h2>Tags</h2>");
        AppendHtmlArray(builder, root, "Tags");
        builder.AppendLine("<h2>Recommendations</h2>");
        AppendHtmlArray(builder, root, "Recommendations");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static void AppendMarkdownValue(StringBuilder builder, string label, JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value))
            builder.AppendLine($"- {label}: `{value.ToString()}`");
    }

    private static void AppendMarkdownObject(StringBuilder builder, JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in value.EnumerateObject())
            builder.AppendLine($"- {property.Name}: `{property.Value.ToString()}`");
    }

    private static void AppendMarkdownArray(StringBuilder builder, JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in value.EnumerateArray())
            builder.AppendLine($"- {item}");
    }

    private static void AppendHtmlParagraph(StringBuilder builder, string label, JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value))
            builder.AppendLine($"<p><strong>{Html(label)}:</strong> {Html(value.ToString())}</p>");
    }

    private static void AppendHtmlDefinitionList(StringBuilder builder, JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return;

        builder.AppendLine("<ul>");
        foreach (var property in value.EnumerateObject())
            builder.AppendLine($"<li><strong>{Html(property.Name)}:</strong> {Html(property.Value.ToString())}</li>");
        builder.AppendLine("</ul>");
    }

    private static void AppendHtmlArray(StringBuilder builder, JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        builder.AppendLine("<ul>");
        foreach (var item in value.EnumerateArray())
            builder.AppendLine($"<li>{Html(item.ToString())}</li>");
        builder.AppendLine("</ul>");
    }

    private static string Csv(string value)
    {
        string escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string Md(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string Html(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
    }

    private static object BuildAssetDependencyTree(OmegaIntelScanResult result)
    {
        var grouped = result.Edges
            .GroupBy(edge => edge.FromId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Node = group.Key,
                Children = group.Select(edge => new
                {
                    edge.ToId,
                    edge.Label
                }).Distinct().ToArray()
            })
            .OrderBy(item => item.Node)
            .ToArray();

        return new
        {
            result.RootPath,
            GeneratedUtc = DateTime.UtcNow,
            Nodes = grouped
        };
    }

    private static object BuildReverseLookup(OmegaIntelScanResult result)
    {
        var grouped = result.Edges
            .GroupBy(edge => edge.ToId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Node = group.Key,
                Sources = group.Select(edge => new
                {
                    edge.FromId,
                    edge.Label
                }).Distinct().ToArray()
            })
            .OrderBy(item => item.Node)
            .ToArray();

        return new
        {
            result.RootPath,
            GeneratedUtc = DateTime.UtcNow,
            Nodes = grouped
        };
    }

    private static object BuildUpkSummary(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .Select(item => new
            {
                item.Path,
                item.Name,
                item.Classification,
                item.Summary,
                item.Details,
                item.Tags,
                item.Insights
            })
            .ToArray();
    }

    private static object BuildExportsByClass(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .SelectMany(item => item.Insights
                .Where(insight => string.Equals(insight.Kind, "ExportClass", StringComparison.OrdinalIgnoreCase))
                .Select(insight => new { item.Path, insight.Value, insight.Details }))
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Class = group.Key,
                Count = group.Count(),
                Files = group.Select(item => item.Path).Distinct().ToArray()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Class)
            .ToArray();
    }

    private static object BuildUpkCrossRefs(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .Select(item => new
            {
                item.Path,
                References = item.Insights
                    .Where(insight => insight.Kind is "ResolvedTexture" or "ResolvedMesh" or "ResolvedAnimSet" or "ResolvedCharacter" or "Content")
                    .Select(insight => new { insight.Kind, insight.Value, insight.Details })
                    .ToArray()
            })
            .ToArray();
    }

    private static object BuildTexturesIndex(OmegaIntelScanResult result)
    {
        var textures = result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Texture)
            .Select(item => new TextureIndexRow
            {
                Path = item.Path,
                Name = item.Name,
                Classification = item.Classification,
                Summary = item.Summary,
                Details = item.Details
            })
            .ToList();

        textures.AddRange(result.TfcMap.Select(entry => new TextureIndexRow
        {
            Path = entry.TfcPath ?? string.Empty,
            Name = entry.TextureName,
            Classification = "Texture / TFC-backed",
            Summary = entry.CacheName,
            Details = entry.TextureFormat ?? string.Empty
        }));

        return textures;
    }

    private static object BuildSkeletons(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .SelectMany(item => item.Insights
                .Where(insight => string.Equals(insight.Kind, "ResolvedMesh", StringComparison.OrdinalIgnoreCase))
                .Select(insight => new
                {
                    item.Path,
                    item.Name,
                    insight.Value,
                    insight.Details
                }))
            .ToArray();
    }

    private static object BuildMeshSkeletonMap(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .SelectMany(item => item.Insights
                .Where(insight => string.Equals(insight.Kind, "ResolvedMesh", StringComparison.OrdinalIgnoreCase))
                .Select(insight => new
                {
                    Mesh = item.Path,
                    Skeleton = insight.Value,
                    Details = insight.Details
                }))
            .ToArray();
    }

    private static object BuildAnimations(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .SelectMany(item => item.Insights
                .Where(insight => string.Equals(insight.Kind, "ResolvedAnimSet", StringComparison.OrdinalIgnoreCase))
                .Select(insight => new
                {
                    item.Path,
                    item.Name,
                    Sequence = insight.Value,
                    insight.Details
                }))
            .ToArray();
    }

    private static object BuildAnimToSkeletonMap(OmegaIntelScanResult result)
    {
        return result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Upk)
            .SelectMany(item => item.Insights
                .Where(insight => string.Equals(insight.Kind, "ResolvedAnimSet", StringComparison.OrdinalIgnoreCase))
                .Select(insight => new
                {
                    AnimSet = item.Path,
                    Skeleton = insight.Details,
                    Sequence = insight.Value
                }))
            .ToArray();
    }

    private static object BuildHeroes(OmegaIntelScanResult result)
    {
        var heroes = result.Files
            .Where(item => item.Kind == OmegaIntelFileKind.Character || item.Kind == OmegaIntelFileKind.Ui)
            .Select(item => new HeroReportRow
            {
                Path = item.Path,
                Name = item.Name,
                Classification = item.Classification,
                Summary = item.Summary,
                Details = item.Details
            })
            .ToList();

        heroes.AddRange(result.UiHeroEntriesList.Select(entry => new HeroReportRow
        {
            Path = entry.Path,
            Name = entry.DisplayName,
            Classification = "UI Hero Entry",
            Summary = entry.HeroId ?? string.Empty,
            Details = entry.Layout ?? string.Empty
        }));

        return heroes;
    }

    private static object BuildHeroAssetMap(OmegaIntelScanResult result)
    {
        return result.UiHeroEntriesList.Select(entry => new
        {
            entry.Path,
            HeroId = entry.HeroId,
            entry.DisplayName,
            entry.Portrait,
            entry.Layout
        }).ToArray();
    }

    private sealed class HeroReportRow
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Classification { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    private sealed class TextureIndexRow
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Classification { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}

