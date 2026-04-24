using OmegaAssetStudio.TfcManifest;

namespace OmegaAssetStudio.Services;

public sealed class TfcManifestEditorService
{
    private readonly TfcManifestReader reader = new();
    private readonly TfcManifestWriter writer = new();

    public TfcManifestDocument Load(string path)
    {
        TfcManifestDocument document = reader.Read(path);
        document.SourceDirectory = Path.GetDirectoryName(path);
        return document;
    }

    public void Save(string path, TfcManifestDocument document)
    {
        writer.Write(path, document);
        document.SourceDirectory = Path.GetDirectoryName(path);
    }

    public void AddEntry(TfcManifestDocument doc, TfcManifestEntry entry)
    {
        doc.Entries.Add(entry);
    }

    public void RemoveEntry(TfcManifestDocument doc, TfcManifestEntry entry)
    {
        doc.Entries.Remove(entry);
    }

    public void UpdateEntry(TfcManifestDocument doc, TfcManifestEntry entry)
    {
        entry.Normalize();
    }

    public void InjectEntries(TfcManifestDocument doc, IEnumerable<TfcManifestEntry> entries)
    {
        foreach (TfcManifestEntry entry in entries)
            doc.Entries.Add(entry);
    }

    public IReadOnlyList<string> Validate(TfcManifestDocument doc)
    {
        List<string> issues = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (TfcManifestEntry entry in doc.Entries)
        {
            string textureKey = BuildTextureKey(entry);
            string duplicateKey = $"{textureKey}|{entry.TfcFileName}|{entry.ChunkIndex}";

            if (string.IsNullOrWhiteSpace(entry.PackageName) && string.IsNullOrWhiteSpace(entry.TextureName))
                issues.Add("Entry is missing a package and texture name.");

            if (string.IsNullOrWhiteSpace(entry.TfcFileName))
                issues.Add($"Entry '{textureKey}' is missing a TFC filename.");

            if (entry.ChunkIndex < 0)
                issues.Add($"Entry '{textureKey}' has an invalid chunk index.");

            if (entry.Offset < 0)
                issues.Add($"Entry '{textureKey}' has a negative offset.");

            if (entry.Size < 0)
                issues.Add($"Entry '{textureKey}' has a negative size.");

            if (!seen.Add(duplicateKey))
                issues.Add($"Duplicate entry detected: {duplicateKey}");

            if (!string.IsNullOrWhiteSpace(doc.SourceDirectory) && !string.IsNullOrWhiteSpace(entry.TfcFileName))
            {
                if (!TryResolveTfcPath(doc.SourceDirectory, entry.TfcFileName, out string? tfcPath))
                    issues.Add($"Missing TFC file for '{textureKey}': {entry.TfcFileName}");
            }

            if (entry.Chunks.Count == 0)
                issues.Add($"Entry '{textureKey}' has no mip/chunk data.");
        }

        return issues;
    }

    private static string BuildTextureKey(TfcManifestEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.PackageName) && !string.IsNullOrWhiteSpace(entry.TextureName))
            return $"{entry.PackageName}.{entry.TextureName}";

        return entry.TextureName ?? string.Empty;
    }

    private static bool TryResolveTfcPath(string sourceDirectory, string tfcFileName, out string? resolvedPath)
    {
        resolvedPath = null;

        if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(tfcFileName))
            return false;

        string[] candidates =
        [
            Path.Combine(sourceDirectory, tfcFileName),
            Path.Combine(sourceDirectory, $"{tfcFileName}.tfc"),
            Path.Combine(sourceDirectory, $"{tfcFileName}.bin")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                resolvedPath = candidate;
                return true;
            }
        }

        return false;
    }
}

