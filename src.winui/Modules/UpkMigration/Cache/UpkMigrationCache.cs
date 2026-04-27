using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkMigrationCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string cacheDirectory;
    private readonly string cachePath;
    private readonly Dictionary<string, UpkMigrationCacheEntry> entries;

    public UpkMigrationCache()
    {
        cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaAssetStudio", "UpkMigration");
        cachePath = Path.Combine(cacheDirectory, "cache.json");
        Directory.CreateDirectory(cacheDirectory);
        entries = LoadEntries();
    }

    public bool TryGetValid(string sourcePath, string schemaVersion, string analyzerVersion, string sourceFingerprint, out UpkMigrationCacheEntry? entry)
    {
        entry = null;
        string key = BuildCacheKey(sourcePath);
        if (!entries.TryGetValue(key, out UpkMigrationCacheEntry? candidate))
            return false;

        if (!string.Equals(candidate.SchemaVersion, schemaVersion, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(candidate.AnalyzerVersion, analyzerVersion, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(candidate.SourceFingerprint, sourceFingerprint, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!File.Exists(candidate.OutputPath))
            return false;

        entry = candidate;
        return true;
    }

    public void Store(UpkMigrationCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entries[BuildCacheKey(entry.SourcePath)] = entry;
        Save();
    }

    public void Clear()
    {
        entries.Clear();

        string backupCachePath = Path.Combine(cacheDirectory, "cache.json.bak");
        if (File.Exists(cachePath))
            File.Delete(cachePath);

        if (File.Exists(backupCachePath))
            File.Delete(backupCachePath);
    }

    public static string ComputeFingerprint(string sourcePath)
    {
        FileInfo info = new(sourcePath);
        string payload = string.Join("|", info.FullName, info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    public static string BuildCacheKey(string sourcePath)
    {
        string normalized = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    private Dictionary<string, UpkMigrationCacheEntry> LoadEntries()
    {
        if (!File.Exists(cachePath))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var list = JsonSerializer.Deserialize<List<UpkMigrationCacheEntry>>(File.ReadAllText(cachePath), JsonOptions) ?? [];
            return list.Where(item => !string.IsNullOrWhiteSpace(item.SourcePath))
                .ToDictionary(item => BuildCacheKey(item.SourcePath), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        File.WriteAllText(cachePath, JsonSerializer.Serialize(entries.Values.OrderBy(item => item.SourcePath).ToArray(), JsonOptions));
    }
}

