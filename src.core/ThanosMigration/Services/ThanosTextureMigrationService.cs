using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using OmegaAssetStudio.ThanosMigration.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosTextureMigrationService
{
    private readonly UpkFileRepository repository = new();

    public async Task<IReadOnlyList<ThanosMigrationFinding>> AnalyzeTextures(string upkPath, Action<double, string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upkPath);

        string fullPath = Path.GetFullPath(upkPath);
        List<ThanosMigrationFinding> findings = [];

        if (!File.Exists(fullPath))
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Critical,
                Category = ThanosMigrationCategory.Textures,
                Message = "Input UPK does not exist.",
                RecommendedAction = "Select a valid Thanos Raid 1.48 UPK."
            });
            return findings;
        }

        try
        {
            progress?.Invoke(0, "Reading texture package...");
            UnrealHeader header = await repository.LoadUpkFile(fullPath).ConfigureAwait(false);
            await header.ReadTablesAsync(_ => { }).ConfigureAwait(false);

            List<UnrealExportTableEntry> textureExports = header.ExportTable
                .Where(export => (export.ClassReferenceNameIndex?.Name ?? string.Empty).Contains("Texture2D", StringComparison.OrdinalIgnoreCase))
                .Where(export => !(export.ObjectNameIndex?.Name ?? string.Empty).StartsWith("Default__", StringComparison.OrdinalIgnoreCase))
                .ToList();

            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Textures,
                Message = $"Detected {textureExports.Count} Texture2D export(s)."
            });

            if (textureExports.Count == 0)
            {
                findings.Add(new ThanosMigrationFinding
                {
                    Severity = ThanosMigrationSeverity.Info,
                    Category = ThanosMigrationCategory.Textures,
                    Message = "No Texture2D exports were detected."
                });
                progress?.Invoke(100, "Texture analysis complete.");
                return findings;
            }

            for (int index = 0; index < textureExports.Count; index++)
            {
                UnrealExportTableEntry export = textureExports[index];
                progress?.Invoke((double)index / Math.Max(1, textureExports.Count) * 100.0, $"Analyzing texture {index + 1} of {textureExports.Count}...");

                try
                {
                    await header.ReadExportObjectAsync(export, _ => { }).ConfigureAwait(false);
                    if (export.UnrealObject is null)
                        await export.ParseUnrealObject(false, false).ConfigureAwait(false);

                    if (export.UnrealObject is not IUnrealObject objectWrapper ||
                        objectWrapper.UObject is not UTexture2D texture)
                    {
                        findings.Add(new ThanosMigrationFinding
                        {
                            Severity = ThanosMigrationSeverity.Warning,
                            Category = ThanosMigrationCategory.Textures,
                            Message = $"Texture export {export.GetPathName()} could not be hydrated.",
                            RecommendedAction = "Verify the source export resolves to a UTexture2D object."
                        });
                        continue;
                    }

                    AnalyzeTextureExport(findings, export, texture);
                }
                catch (Exception ex)
                {
                    findings.Add(new ThanosMigrationFinding
                    {
                        Severity = ThanosMigrationSeverity.Warning,
                        Category = ThanosMigrationCategory.Textures,
                        Message = $"Texture export {export.GetPathName()} failed to analyze: {ex.Message}",
                        RecommendedAction = "Verify the package contains a readable Texture2D export."
                    });
                }
            }

            progress?.Invoke(100, "Texture analysis complete.");
        }
        catch (Exception ex)
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Critical,
                Category = ThanosMigrationCategory.Textures,
                Message = $"Failed to inspect texture exports: {ex.Message}",
                RecommendedAction = "Verify the package is readable and contains valid texture exports."
            });
        }

        return findings;
    }

    private static void AnalyzeTextureExport(List<ThanosMigrationFinding> findings, UnrealExportTableEntry export, UTexture2D texture)
    {
        bool hasEmbeddedMips = texture.Mips?.Any(mip => mip?.Data is { Length: > 0 }) == true;
        bool hasCachedMips =
            texture.CachedPVRTCMips?.Any(mip => mip?.Data is { Length: > 0 }) == true ||
            texture.CachedATITCMips?.Any(mip => mip?.Data is { Length: > 0 }) == true ||
            texture.CachedETCMips?.Any(mip => mip?.Data is { Length: > 0 }) == true ||
            texture.CachedFlashMipData is { Length: > 0 };

        string textureFileCacheName = texture.TextureFileCacheName?.Name ?? string.Empty;
        bool hasTextureFileCache = !string.IsNullOrWhiteSpace(textureFileCacheName);

        findings.Add(new ThanosMigrationFinding
        {
            Severity = ThanosMigrationSeverity.Info,
            Category = hasTextureFileCache ? ThanosMigrationCategory.Tfc : ThanosMigrationCategory.Textures,
            Message = $"Texture {export.GetPathName()} detected as {(hasTextureFileCache ? "TFC-backed" : "embedded")} {texture.SizeX}x{texture.SizeY} {texture.Format}.",
            Offset = export.SerialDataOffset
        });

        if (hasTextureFileCache)
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Tfc,
                Message = $"Texture {export.GetPathName()} references TFC entry {textureFileCacheName}.",
                RecommendedAction = "Verify the matching TFC manifest entry is present in the 1.52 client."
            });
        }
        else if (!hasEmbeddedMips && !hasCachedMips)
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Critical,
                Category = ThanosMigrationCategory.Textures,
                Message = $"Texture {export.GetPathName()} has no readable mip data.",
                RecommendedAction = "Verify the export is not stripped or corrupted before migration."
            });
        }
        else if (hasEmbeddedMips)
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Textures,
                Message = $"Texture {export.GetPathName()} contains embedded mip data."
            });
        }

        if (texture.Format == EPixelFormat.PF_Unknown)
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Warning,
                Category = ThanosMigrationCategory.Textures,
                Message = $"Texture {export.GetPathName()} uses an unknown pixel format.",
                RecommendedAction = "Verify the texture format resolves correctly before streaming migration."
            });
        }

        if (texture.MipTailBaseIdx >= 0)
        {
            findings.Add(new ThanosMigrationFinding
            {
                Severity = ThanosMigrationSeverity.Info,
                Category = ThanosMigrationCategory.Textures,
                Message = $"Texture {export.GetPathName()} reports MipTailBaseIdx={texture.MipTailBaseIdx}.",
                Offset = export.SerialDataOffset
            });
        }
    }
}

