using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using UpkManager.Models.UpkFile.Engine.Anim;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;

public sealed class Upk148AssetBundle
{
    public List<Upk148ExportTableEntry> SkeletalMeshes { get; } = [];
    public List<Upk148ExportTableEntry> StaticMeshes { get; } = [];
    public List<Upk148ExportTableEntry> Textures { get; } = [];
    public List<Upk148ExportTableEntry> Animations { get; } = [];
    public List<Upk148ExportTableEntry> Materials { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
}

public sealed class Upk148AssetExtractor
{
    private static readonly HashSet<string> SupportedAssetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkeletalMesh",
        "StaticMesh",
        "Texture2D",
        "AnimSequence",
        "AnimSet",
        "Material",
        "MaterialInstance",
        "MaterialInstanceConstant"
    };

    public Upk148AssetBundle Extract(Upk148Document document, Action<string>? log = null)
    {
        Upk148AssetBundle bundle = new();
        WarningBatch warnings = new();
        foreach (Upk148ExportTableEntry export in document.Header.ExportTable.Entries)
        {
            if (!IsSupportedAssetClass(export))
                continue;

            if (TryHydrateSupportedExport(export, out object? resolved) &&
                TryClassifyResolved(export, resolved, bundle))
            {
                continue;
            }

            if (TryClassifyByClassName(export, bundle))
                continue;

            warnings.Add("Warning: supported export could not be hydrated", export.PathName, export.ClassName);
        }

        warnings.Flush(log);
        log?.Invoke($"Extracted {bundle.SkeletalMeshes.Count} skeletal mesh, {bundle.StaticMeshes.Count} static mesh, {bundle.Textures.Count} texture, {bundle.Animations.Count} animation, and {bundle.Materials.Count} material export(s).");
        return bundle;
    }

    private static bool IsSupportedAssetClass(Upk148ExportTableEntry export)
    {
        string classKey = NormalizeClassKey(export.ClassName);
        if (string.IsNullOrWhiteSpace(classKey))
            return false;

        if (export.ObjectName.StartsWith("Default__", StringComparison.OrdinalIgnoreCase))
            return false;

        return SupportedAssetClasses.Contains(classKey);
    }

    private static bool TryHydrateSupportedExport(Upk148ExportTableEntry export, out object? resolved)
    {
        resolved = null;

        return NormalizeClassKey(export.ClassName) switch
        {
            "SKELETALMESH" => TryHydrateExport<USkeletalMesh>(export, out resolved),
            "STATICMESH" => TryHydrateExport<UStaticMesh>(export, out resolved),
            "TEXTURE2D" => TryHydrateExport<UTexture2D>(export, out resolved),
            "ANIMSEQUENCE" => TryHydrateExport<UAnimSequence>(export, out resolved),
            "ANIMSET" => TryHydrateExport<UAnimSet>(export, out resolved),
            "MATERIAL" => TryHydrateExport<UMaterial>(export, out resolved),
            "MATERIALINSTANCE" => TryHydrateExport<UMaterialInstance>(export, out resolved),
            "MATERIALINSTANCECONSTANT" => TryHydrateExport<UMaterialInstanceConstant>(export, out resolved),
            _ => false
        };
    }

    private static bool TryHydrateExport<T>(Upk148ExportTableEntry export, out object? resolved)
        where T : class
    {
        resolved = null;

        if (!UpkExportHydrator.TryHydrate(export, out T? typed, null, false) || typed is null)
            return false;

        resolved = typed;
        return true;
    }

    private static bool TryClassifyResolved(Upk148ExportTableEntry export, object? resolved, Upk148AssetBundle bundle)
    {
        if (resolved is USkeletalMesh)
        {
            bundle.SkeletalMeshes.Add(export);
            return true;
        }

        if (resolved is UStaticMesh)
        {
            bundle.StaticMeshes.Add(export);
            return true;
        }

        if (resolved is UTexture2D)
        {
            bundle.Textures.Add(export);
            return true;
        }

        if (resolved is UAnimSequence || resolved is UAnimSet)
        {
            bundle.Animations.Add(export);
            return true;
        }

        if (resolved is UMaterial || resolved is UMaterialInstance || resolved is UMaterialInstanceConstant)
        {
            bundle.Materials.Add(export);
            return true;
        }

        return false;
    }

    private static bool TryClassifyByClassName(Upk148ExportTableEntry export, Upk148AssetBundle bundle)
    {
        switch (NormalizeClassKey(export.ClassName))
        {
            case "SKELETALMESH":
                bundle.SkeletalMeshes.Add(export);
                return true;
            case "STATICMESH":
                bundle.StaticMeshes.Add(export);
                return true;
            case "TEXTURE2D":
                bundle.Textures.Add(export);
                return true;
            case "ANIMSEQUENCE":
            case "ANIMSET":
                bundle.Animations.Add(export);
                return true;
            case "MATERIAL":
            case "MATERIALINSTANCE":
            case "MATERIALINSTANCECONSTANT":
                bundle.Materials.Add(export);
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeClassKey(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return string.Empty;

        Span<char> buffer = stackalloc char[className.Length];
        int count = 0;
        foreach (char character in className)
        {
            if (!char.IsLetterOrDigit(character))
                continue;

            buffer[count++] = char.ToUpperInvariant(character);
        }

        return count == 0 ? string.Empty : new string(buffer[..count]);
    }

    private sealed class WarningBatch
    {
        private readonly Dictionary<string, WarningBucket> buckets = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string category, string path, string? reason)
        {
            string key = string.IsNullOrWhiteSpace(reason) ? category : $"{category}::{reason}";
            if (!buckets.TryGetValue(key, out WarningBucket? bucket))
            {
                bucket = new WarningBucket(category, reason);
                buckets[key] = bucket;
            }

            bucket.Count++;
            bucket.AddExample(path);
        }

        public void Flush(Action<string>? log)
        {
            if (log is null || buckets.Count == 0)
                return;

            foreach (WarningBucket bucket in buckets.Values)
                log(bucket.Format());

            buckets.Clear();
        }
    }

    private sealed class WarningBucket
    {
        private const int MaxExamples = 3;
        private readonly List<string> examples = [];

        public WarningBucket(string category, string? reason)
        {
            Category = category;
            Reason = reason;
        }

        public string Category { get; }

        public string? Reason { get; }

        public int Count { get; set; }

        public void AddExample(string path)
        {
            if (examples.Count >= MaxExamples)
                return;

            if (!string.IsNullOrWhiteSpace(path) && !examples.Contains(path, StringComparer.OrdinalIgnoreCase))
                examples.Add(path);
        }

        public string Format()
        {
            string message = Count == 1 ? Category : $"{Category}: {Count} export(s)";
            if (!string.IsNullOrWhiteSpace(Reason))
                message += $": {Reason}";

            if (examples.Count > 0)
                message += $" | Examples: {string.Join("; ", examples)}";

            return message;
        }
    }
}

