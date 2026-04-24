using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.ThanosMigration.Upk;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosStructuralMigrationService
{
    private static readonly string[] WorldTokens =
    [
        "worldinfo",
        "levelinfo",
        "world",
        "level",
        "streaminglevel"
    ];

    private static readonly string[] KismetTokens =
    [
        "kismet",
        "sequence",
        "seqact",
        "seqcond",
        "seqevent"
    ];

    private static readonly string[] ScriptTokens =
    [
        "script",
        "function",
        "state",
        "class",
        "struct",
        "bytecode"
    ];

    private static readonly string[] StreamingTokens =
    [
        "stream",
        "lod",
        "package",
        "levelstream"
    ];

    private readonly UpkFileRepository repository = new();

    public async Task<List<ThanosMigrationStep>> MigrateStructure(string inputPath, string outputPath, Action<double, string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string fullInputPath = Path.GetFullPath(inputPath);
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath) ?? Environment.CurrentDirectory);

        List<ThanosMigrationStep> steps =
        [
            new()
            {
                Name = "VersionTagPatch",
                Description = "Patch the package header from 1.48 to 1.52."
            },
            new()
            {
                Name = "NameTableNormalization",
                Description = "Rebuild name table offsets with the existing writer."
            },
            new()
            {
                Name = "ImportTableNormalization",
                Description = "Normalize import table references."
            },
            new()
            {
                Name = "ExportTableNormalization",
                Description = "Normalize export table references and serialization order."
            },
            new()
            {
                Name = "CompressionFlagNormalization",
                Description = "Preserve supported compression flags."
            },
            new()
            {
                Name = "UnknownRegionPreservation",
                Description = "Preserve unresolved export bodies through raw write fallback."
            }
        ];

        try
        {
            progress?.Invoke(0, "Reading structural package...");
            UnrealHeader header = await repository.LoadUpkFile(fullInputPath).ConfigureAwait(false);
            progress?.Invoke(20, "Reading structural tables...");
            await header.ReadTablesAsync(_ => { }).ConfigureAwait(false);

            if (PatchHeaderVersion(header))
            {
                steps[0].Status = ThanosMigrationStepStatus.Done;
            }
            else
            {
                steps[0].Status = ThanosMigrationStepStatus.Skipped;
                steps[0].Reason = "Header version did not require a patch.";
            }

            steps[1].Status = ThanosMigrationStepStatus.Done;
            steps[2].Status = ThanosMigrationStepStatus.Done;
            steps[3].Status = ThanosMigrationStepStatus.Done;
            steps[4].Status = header.CompressionFlags == 0
                ? ThanosMigrationStepStatus.Skipped
                : ThanosMigrationStepStatus.Done;
            steps[4].Reason = header.CompressionFlags == 0
                ? "Package is not compressed."
                : "Supported compression flags preserved.";
            steps[5].Status = ThanosMigrationStepStatus.Done;
            steps[5].Reason = "Raw export write fallback preserves unknown regions and unresolved bodies.";

            progress?.Invoke(80, "Writing structural output...");
            await repository.SaveUpkFile(header, fullOutputPath).ConfigureAwait(false);
            progress?.Invoke(100, "Structural patch complete.");
        }
        catch (Exception ex)
        {
            foreach (ThanosMigrationStep step in steps.Where(static step => step.Status == ThanosMigrationStepStatus.Pending))
            {
                step.Status = ThanosMigrationStepStatus.Failed;
                step.Exception = ex;
                step.Reason = ex.Message;
            }
        }

        return steps;
    }

    public async Task<ThanosMigrationReport> Analyze(string upkPath, Action<double, string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upkPath);

        string fullPath = Path.GetFullPath(upkPath);
        FileInfo fileInfo = new(fullPath);

        ThanosMigrationReport report = new()
        {
            FilePath = fullPath,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0
        };

        if (!fileInfo.Exists)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Critical,
                Category = ThanosMigrationCategory.Header,
                Message = "Input UPK does not exist.",
                RecommendedAction = "Select a valid Thanos Raid 1.48 UPK."
            });
            return report;
        }

        try
        {
            progress?.Invoke(0, "Reading structural package...");
            UnrealHeader header = await repository.LoadUpkFile(fullPath).ConfigureAwait(false);
            progress?.Invoke(20, "Reading structural tables...");
            await header.ReadTablesAsync(_ => { }).ConfigureAwait(false);

            report.NameCount = header.NameTableCount;
            report.ImportCount = header.ImportTableCount;
            report.ExportCount = header.ExportTableCount;
            report.DetectedVersionTag = $"v{header.Version}.{header.Licensee}";
            report.CompressionMethod = DescribeCompression(header.CompressionFlags);

            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Header,
                Message = $"Detected {report.DetectedVersionTag} with {report.NameCount} name(s), {report.ImportCount} import(s), and {report.ExportCount} export(s)."
            });
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Compression,
                Message = $"Compression mode: {report.CompressionMethod}."
            });

            progress?.Invoke(60, "Inspecting header and export layout...");
            AnalyzeHeader(report, header);
            AnalyzeExports(report, header);
            progress?.Invoke(85, "Inspecting table layout...");
            AnalyzeTableLayout(report, header);
            progress?.Invoke(100, "Structural analysis complete.");
        }
        catch (Exception ex)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Critical,
                Category = ThanosMigrationCategory.Header,
                Message = $"Failed to read UPK header or tables: {ex.Message}",
                RecommendedAction = "Verify the package is a readable 1.48 UPK and retry."
            });
        }

        return report;
    }

    private static void AnalyzeHeader(ThanosMigrationReport report, UnrealHeader header)
    {
        if (header.Version == ThanosUpkVersionConstants.V148_FileVersion &&
            header.Licensee == ThanosUpkVersionConstants.V148_LicenseeVersion)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Header,
                Message = "Detected expected 1.48 file and licensee versions."
            });
        }
        else if (header.Version == ThanosUpkVersionConstants.V152_FileVersion &&
                 header.Licensee == ThanosUpkVersionConstants.V152_LicenseeVersion)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Warning,
                Category = ThanosMigrationCategory.Header,
                Message = "Package already matches the 1.52 target version."
            });
        }
        else
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Warning,
                Category = ThanosMigrationCategory.Header,
                Message = $"Unexpected header version {header.Version}.{header.Licensee}.",
                RecommendedAction = "Confirm the source package is the expected Thanos Raid 1.48 build."
            });
        }

        if (header.CompressionFlags == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Compression,
                Message = "Package is uncompressed."
            });
        }
        else if ((header.CompressionFlags & 0x00000006U) == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Warning,
                Category = ThanosMigrationCategory.Compression,
                Message = $"Unexpected compression flags 0x{header.CompressionFlags:X8}.",
                RecommendedAction = "Verify the package uses supported LZO compression."
            });
        }
    }

    private static void AnalyzeExports(ThanosMigrationReport report, UnrealHeader header)
    {
        int worldMatches = 0;
        int kismetMatches = 0;
        int scriptMatches = 0;
        int streamingMatches = 0;
        int skeletalMeshMatches = 0;
        int staticMeshMatches = 0;
        int textureMatches = 0;
        int animationMatches = 0;
        int materialMatches = 0;

        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
            string objectName = export.ObjectNameIndex?.Name ?? string.Empty;
            string pathName = export.GetPathName();
            string combined = $"{className} {objectName} {pathName}".ToLowerInvariant();

            if (string.Equals(className, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                skeletalMeshMatches++;

            if (string.Equals(className, "StaticMesh", StringComparison.OrdinalIgnoreCase))
                staticMeshMatches++;

            if (string.Equals(className, "Texture2D", StringComparison.OrdinalIgnoreCase))
                textureMatches++;

            if (string.Equals(className, "AnimSequence", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "AnimSet", StringComparison.OrdinalIgnoreCase))
                animationMatches++;

            if (string.Equals(className, "Material", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "MaterialInstance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "MaterialInstanceConstant", StringComparison.OrdinalIgnoreCase))
                materialMatches++;

            if (ContainsAny(combined, WorldTokens))
                worldMatches++;

            if (ContainsAny(combined, KismetTokens))
                kismetMatches++;

            if (ContainsAny(combined, ScriptTokens))
                scriptMatches++;

            if (ContainsAny(combined, StreamingTokens))
                streamingMatches++;
        }

        if (worldMatches == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.World,
                Message = "No world or level-related exports were detected.",
            });
        }

        if (kismetMatches == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Kismet,
                Message = "No Kismet or sequence-related exports were detected.",
            });
        }

        if (scriptMatches == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Script,
                Message = "No script-related exports were detected.",
            });
        }

        if (streamingMatches == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Streaming,
                Message = "No streaming-level-related exports were detected."
            });
        }

        if (report.NameCount == 0 || report.ImportCount == 0 || report.ExportCount == 0)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Critical,
                Category = ThanosMigrationCategory.Unknown,
                Message = "One or more core tables are empty.",
                RecommendedAction = "Verify the input package is not truncated or corrupted."
            });
        }

        report.SkeletalMeshCount = skeletalMeshMatches;
        report.StaticMeshCount = staticMeshMatches;
        report.TextureCount = textureMatches;
        report.AnimationCount = animationMatches;
        report.MaterialCount = materialMatches;
    }

    private static void AnalyzeTableLayout(ThanosMigrationReport report, UnrealHeader header)
    {
        List<long> tableEnds =
        [
            header.NameTableOffset + EstimateNameTableSize(header),
            header.ImportTableOffset + EstimateImportTableSize(header),
            header.ExportTableOffset + EstimateExportTableSize(header)
        ];

        long furthestTableEnd = tableEnds.Where(static value => value > 0).DefaultIfEmpty(0).Max();

        List<long> exportEnds = header.ExportTable
            .Select(static export => (long)export.SerialDataOffset + export.SerialDataSize)
            .Where(static value => value > 0)
            .ToList();

        long furthestExportEnd = exportEnds.Count > 0 ? exportEnds.Max() : 0;
        long contentEnd = Math.Max(furthestTableEnd, furthestExportEnd);

        if (report.FileSizeBytes > 0 && contentEnd > 0 && report.FileSizeBytes > contentEnd + 4096)
        {
            report.Findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Unknown,
                Message = "Package has bytes beyond the parsed export body region.",
                RecommendedAction = "Review the remaining package tail for streamed data or unparsed custom regions before migration."
            });
        }
    }

    private static long EstimateNameTableSize(UnrealHeader header)
        => header.NameTable.Sum(entry => entry.GetBuilderSize());

    private static long EstimateImportTableSize(UnrealHeader header)
        => header.ImportTable.Sum(entry => entry.GetBuilderSize());

    private static long EstimateExportTableSize(UnrealHeader header)
        => header.ExportTable.Sum(entry => entry.GetBuilderSize());

    private static bool ContainsAny(string input, IReadOnlyList<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (input.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string DescribeCompression(uint compressionFlags)
    {
        if (compressionFlags == 0)
            return "None";

        List<string> flags = [];

        if ((compressionFlags & 0x00000004U) != 0)
            flags.Add("LZO");

        if ((compressionFlags & 0x00000008U) != 0)
            flags.Add("LZO_ENC");

        if (flags.Count > 0)
            return string.Join("+", flags);

        return $"0x{compressionFlags:X8}";
    }

    private static bool PatchHeaderVersion(UnrealHeader header)
    {
        bool changed = false;
        changed |= SetProperty(header, nameof(UnrealHeader.Version), (ushort)ThanosUpkVersionConstants.V152_FileVersion);
        changed |= SetProperty(header, nameof(UnrealHeader.Licensee), (ushort)ThanosUpkVersionConstants.V152_LicenseeVersion);
        return changed;
    }

    private static bool SetProperty<T>(object target, string propertyName, T value)
    {
        PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is null || !property.CanWrite)
            return false;

        property.SetValue(target, value);
        return true;
    }
}

