using DDSLib;
using DDSLib.Constants;
using OmegaAssetStudio.TextureManager;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Textures2;

public sealed class UpkParser
{
    public async Task<UpkMetadata> ParseAsync(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("UPK path is required.", nameof(upkPath));

        UpkFileRepository repository = new();
        UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        IReadOnlyList<string> names = header.NameTable
            .Select(entry => entry?.Name?.String ?? string.Empty)
            .ToArray();

        IReadOnlyList<string> imports = header.ImportTable
            .Select(entry => entry?.GetPathName() ?? string.Empty)
            .ToArray();

        IReadOnlyList<ExportInfo> exports = header.ExportTable
            .Select(export =>
            {
                string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
                string superName = export.SuperReferenceNameIndex?.Name ?? string.Empty;
                string outerName = export.OuterReferenceNameIndex?.Name ?? string.Empty;
                bool isTexture2D = IsTexture2DExport(export, className, superName, outerName);

                return new ExportInfo(
                    export.TableIndex,
                    export.ObjectNameIndex?.Name ?? string.Empty,
                    export.GetPathName(),
                    className,
                    superName,
                    outerName,
                    export.SerialDataOffset,
                    export.SerialDataSize,
                    export.PackageFlags,
                    isTexture2D);
            })
            .ToArray();

        return new UpkMetadata
        {
            FilePath = upkPath,
            PackageName = Path.GetFileNameWithoutExtension(upkPath),
            FileSize = (int)new FileInfo(upkPath).Length,
            IsCompressed = header.CompressedChunks.Count > 0,
            NameTableCount = header.NameTableCount,
            ImportTableCount = header.ImportTableCount,
            ExportTableCount = header.ExportTableCount,
            NameTable = names,
            Imports = imports,
            Exports = exports
        };
    }

    private static bool IsTexture2DExport(UnrealExportTableEntry export, string className, string superName, string outerName)
    {
        if (IsTexture2DClassName(className) || IsTexture2DClassName(superName) || IsTexture2DClassName(outerName))
            return true;

        try
        {
            if (export.UnrealObject == null)
                export.ParseUnrealObject(false, false).GetAwaiter().GetResult();

            if (export.UnrealObject is IUnrealObject unrealObject && unrealObject.UObject is UTexture2D)
                return true;
        }
        catch
        {
            // Leave the export in the list if it looks texture-like elsewhere.
        }

        return false;
    }

    private static bool IsTexture2DClassName(string value)
    {
        return string.Equals(value, "Texture2D", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "UTexture2D", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Texture2D", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class Texture2DExtractor
{
    private readonly UpkTextureLoader _loader = new();

    public async Task<Texture2DInfo> ExtractAsync(string upkPath, string exportPath, TextureType textureType = TextureType.Diffuse, int? requestedMipIndex = null, Action<string>? log = null)
    {
        TexturePreviewTexture texture = await _loader.LoadFromUpkAsync(
            upkPath,
            exportPath,
            MapSlot(textureType),
            log,
            requestedMipIndex).ConfigureAwait(true);

        return ToTextureInfo(texture, textureType);
    }

    public static Texture2DInfo ToTextureInfo(TexturePreviewTexture texture, TextureType textureType)
    {
        return new Texture2DInfo
        {
            Name = texture.Name,
            SourcePath = texture.SourcePath,
            ExportPath = texture.ExportPath,
            SourceDescription = texture.SourceDescription,
            Format = texture.Format,
            Compression = texture.Compression,
            ContainerType = texture.ContainerType,
            Width = texture.Width,
            Height = texture.Height,
            MipCount = texture.MipCount,
            SelectedMipIndex = texture.SelectedMipIndex,
            MipSource = texture.MipSource,
            TextureType = textureType,
            Mips = texture.AvailableMipLevels.Select(level => new MipInfo(
                level.AbsoluteIndex,
                level.RelativeIndex,
                level.Width,
                level.Height,
                level.DataSize,
                level.Source,
                level.Format)).ToArray()
        };
    }

    private static TexturePreviewMaterialSlot MapSlot(TextureType textureType)
    {
        return textureType switch
        {
            TextureType.Normal => TexturePreviewMaterialSlot.Normal,
            TextureType.Specular => TexturePreviewMaterialSlot.Specular,
            TextureType.Emissive => TexturePreviewMaterialSlot.Emissive,
            TextureType.Mask => TexturePreviewMaterialSlot.Mask,
            _ => TexturePreviewMaterialSlot.Diffuse
        };
    }
}

public sealed class DdsService
{
    public static FileFormat SuggestFormat(TextureType textureType, bool hasAlpha)
    {
        return textureType switch
        {
            TextureType.Normal => FileFormat.BC5,
            TextureType.Specular => FileFormat.BC7,
            TextureType.Emissive => FileFormat.BC7,
            TextureType.Mask => hasAlpha ? FileFormat.BC7 : FileFormat.DXT1,
            TextureType.UI => hasAlpha ? FileFormat.A8R8G8B8 : FileFormat.DXT1,
            TextureType.FX => hasAlpha ? FileFormat.A8R8G8B8 : FileFormat.DXT1,
            _ => hasAlpha ? FileFormat.BC7 : FileFormat.DXT1
        };
    }

    public static string SuggestFormatLabel(TextureType textureType, bool hasAlpha, string? sourceName = null)
    {
        string name = (sourceName ?? string.Empty).ToLowerInvariant();
        bool looksLikeNormal = name.Contains("normal") || name.Contains("_n") || name.Contains("_nm") || name.Contains("nrm") || name.Contains("bump");
        bool looksLikeMask = name.Contains("mask") || name.Contains("opacity") || name.Contains("alpha") || name.Contains("occlusion");
        bool looksLikeSpec = name.Contains("spec") || name.Contains("gloss") || name.Contains("refl") || name.Contains("reflection");

        if (textureType == TextureType.Normal || looksLikeNormal)
            return "BC5 / DXT5nm";

        if ((textureType == TextureType.Mask || textureType == TextureType.UI || textureType == TextureType.FX) && (hasAlpha || looksLikeMask))
            return "BC7";

        if ((textureType == TextureType.Specular || textureType == TextureType.Emissive) && (hasAlpha || looksLikeSpec))
            return "BC7";

        if (hasAlpha)
            return "BC7";

        return "DXT1";
    }

    public static FileFormat ResolveFormat(TexturePreviewTexture texture)
    {
        string format = (texture.Format ?? string.Empty).ToUpperInvariant();
        if (format.Contains("DXT1")) return FileFormat.DXT1;
        if (format.Contains("DXT3")) return FileFormat.DXT3;
        if (format.Contains("DXT5")) return FileFormat.DXT5;
        if (format.Contains("BC7")) return FileFormat.BC7;
        if (format.Contains("A8R8G8B8")) return FileFormat.A8R8G8B8;
        if (format.Contains("X8R8G8B8")) return FileFormat.X8R8G8B8;
        if (format.Contains("A8B8G8R8")) return FileFormat.A8B8G8R8;
        if (format.Contains("X8B8G8R8")) return FileFormat.X8B8G8R8;
        if (format.Contains("R8G8B8")) return FileFormat.R8G8B8;
        if (format.Contains("R5G6B5")) return FileFormat.R5G6B5;
        if (format.Contains("G8")) return FileFormat.G8;
        if (format.Contains("V8U8")) return FileFormat.V8U8;
        return FileFormat.DXT5;
    }

    public static DdsFile BuildDds(TexturePreviewTexture texture, int? mipCountOverride = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        FileFormat format = texture.Slot == TexturePreviewMaterialSlot.Normal ? FileFormat.BC5 : ResolveFormat(texture);
        return BuildDds(texture.Width, texture.Height, texture.RgbaPixels, format, mipCountOverride ?? Math.Max(1, texture.MipCount));
    }

    public static DdsFile BuildDds(int width, int height, byte[] rgbaPixels, FileFormat format, int mipCount = 1)
    {
        return DdsFile.FromRgba(width, height, rgbaPixels, format, Math.Max(1, mipCount));
    }

    public static TextureFileFormat DetectFormat(string filePath)
    {
        return TextureFormatDetector.DetectFormat(filePath);
    }

    public static byte[] BuildDdsHeader(int width, int height, FileFormat format, int mipCount = 1)
    {
        byte[] rgba = new byte[Math.Max(1, width * height) * 4];
        DdsFile dds = DdsFile.FromRgba(width, height, rgba, format, mipCount);
        using MemoryStream stream = new();
        dds.Save(stream, new DdsSaveConfig(format, 0, 0, false, false));
        int headerSize = format == FileFormat.BC7 ? 148 : 128;
        return stream.ToArray()[..Math.Min(headerSize, (int)stream.Length)];
    }

    public static byte[] StripDdsHeader(byte[] ddsBytes)
    {
        if (ddsBytes is null || ddsBytes.Length < 128)
            throw new ArgumentException("DDS payload is too small.", nameof(ddsBytes));

        if (ddsBytes[0] != 0x44 || ddsBytes[1] != 0x44 || ddsBytes[2] != 0x53 || ddsBytes[3] != 0x20)
            throw new InvalidOperationException("The payload does not start with a DDS header.");

        return ddsBytes[128..];
    }
}

public sealed class InlineMipService
{
    private readonly UpkTextureLoader _loader = new();
    private readonly TexturePreviewInjector _injector = new();
    private readonly TextureLoader _diskLoader = new();

    public async Task<string> ExtractInlineMipAsync(string upkPath, string exportPath, int? requestedMipIndex, string outputDirectory, Action<string>? log = null)
    {
        TexturePreviewTexture texture = await _loader.LoadFromUpkAsync(
            upkPath,
            exportPath,
            TexturePreviewMaterialSlot.Diffuse,
            log,
            requestedMipIndex).ConfigureAwait(true);

        Directory.CreateDirectory(outputDirectory);
        int mipIndex = texture.SelectedMipIndex;
        string fileName = $"{SanitizeFileName(texture.Name)}_mip{mipIndex}_{texture.Width}x{texture.Height}.dds";
        string outputPath = Path.Combine(outputDirectory, fileName);

        byte[] ddsBytes = texture.ContainerBytes is { Length: > 0 }
            ? texture.ContainerBytes
            : SaveAsDds(texture);

        await File.WriteAllBytesAsync(outputPath, ddsBytes).ConfigureAwait(true);
        log?.Invoke($"Extracted mip {mipIndex} to {outputPath}.");
        return outputPath;
    }

    public async Task ReplaceInlineMipAsync(string upkPath, string exportPath, string replacementPath, TexturePreviewMaterialSlot slot = TexturePreviewMaterialSlot.Diffuse, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(replacementPath))
            throw new ArgumentException("Replacement path is required.", nameof(replacementPath));

        TexturePreviewTexture replacement = _diskLoader.LoadFromFile(replacementPath, slot);
        await _injector.InjectInlineAsync(upkPath, exportPath, replacement, log).ConfigureAwait(true);
    }

    public Task<string> ReplaceInlineMipFromDdsAsync(string upkPath, string ddsPath, bool inplace = false, Action<string>? log = null)
    {
        return _injector.ReplaceInlineMipFromDdsAsync(upkPath, ddsPath, inplace, log);
    }

    private static byte[] SaveAsDds(TexturePreviewTexture texture)
    {
        DdsFile dds = DdsService.BuildDds(texture);
        using MemoryStream stream = new();
        dds.Save(stream, new DdsSaveConfig(DdsService.ResolveFormat(texture), 0, 0, false, false));
        return stream.ToArray();
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "texture" : value;
    }
}

public sealed class TfcService
{
    public void Initialize()
    {
        TextureManifest.Initialize();
        TextureFileCache.Initialize();
    }

    public int LoadManifest(string manifestPath) => TextureManifest.Instance.LoadManifest(manifestPath);
    public void SaveManifest(string? manifestPath = null) => TextureManifest.Instance.SaveManifest(manifestPath);

    public string ExtractCurrentCache(string outputDirectory, Action<string>? log = null)
    {
        EnsureCacheReady();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        string textureName = SanitizeFileName(TextureFileCache.Instance.Entry.Head.TextureName);
        string outputPath = Path.Combine(outputDirectory, $"{textureName}.dds");

        using Stream stream = TextureFileCache.Instance.Texture2D.GetMipMapsStream() ?? throw new InvalidOperationException("Current cache does not expose mip data.");
        stream.Position = 0;
        using FileStream fileStream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(fileStream);
        log?.Invoke($"Extracted cache to {outputPath}.");
        return outputPath;
    }

    public string RebuildCurrentCache(Action<string>? log = null)
    {
        EnsureCacheReady();
        string cacheName = TextureFileCache.Instance.Entry.Data.TextureFileName;
        string cacheRoot = TextureManifest.Instance.ManifestPath;
        DdsFile dds = LoadCurrentCacheDds(log);
        WriteResult result = TextureFileCache.Instance.WriteTexture(cacheRoot, cacheName, ImportType.Replace, dds);
        if (result != WriteResult.Success)
            throw new InvalidOperationException($"Rebuild failed with result '{result}'.");

        TextureManifest.Instance.SaveManifest();
        string outputPath = Path.Combine(cacheRoot, cacheName + ".tfc");
        log?.Invoke($"Rebuilt cache: {outputPath}.");
        return outputPath;
    }

    public string DefragmentCurrentCache(Action<string>? log = null)
    {
        EnsureCacheReady();
        string cacheName = TextureFileCache.Instance.Entry.Data.TextureFileName;
        string cacheRoot = TextureManifest.Instance.ManifestPath;
        string tempRoot = Path.Combine(Path.GetTempPath(), "OmegaAssetStudio_TextureDefrag");
        Directory.CreateDirectory(tempRoot);

        DdsFile dds = LoadCurrentCacheDds(log);
        WriteResult result = TextureFileCache.Instance.WriteTexture(tempRoot, cacheName, ImportType.New, dds);
        if (result != WriteResult.Success)
            throw new InvalidOperationException($"Defragment failed with result '{result}'.");

        string tempPath = Path.Combine(tempRoot, cacheName + ".tfc");
        string finalPath = Path.Combine(cacheRoot, cacheName + ".tfc");
        File.Copy(tempPath, finalPath, overwrite: true);
        TextureManifest.Instance.SaveManifest();
        log?.Invoke($"Defragmented cache: {finalPath}.");
        return finalPath;
    }

    public bool TryResolveEntry(FObject textureObject, out TextureEntry entry)
    {
        if (TextureManifest.Instance is null)
        {
            entry = default!;
            return false;
        }

        entry = TextureManifest.Instance.GetTextureEntryFromObject(textureObject);
        return entry != null;
    }

    public bool LoadTextureCache(string tfcPath, TextureEntry entry, bool onlyFirst = false)
    {
        return TextureFileCache.Instance.LoadFromFile(tfcPath, entry, onlyFirst);
    }

    public WriteResult WriteTexture(string texturePath, string textureCacheName, ImportType importType, DdsFile ddsHeader)
    {
        return TextureFileCache.Instance.WriteTexture(texturePath, textureCacheName, importType, ddsHeader);
    }

    private static DdsFile LoadCurrentCacheDds(Action<string>? log)
    {
        EnsureCacheReady();
        using Stream stream = TextureFileCache.Instance.Texture2D.GetMipMapsStream() ?? throw new InvalidOperationException("Current cache does not expose mip data.");
        log?.Invoke("Loading current cache as DDS.");
        stream.Position = 0;
        DdsFile dds = new();
        dds.Load(stream);
        return dds;
    }

    private static void EnsureCacheReady()
    {
        if (TextureManifest.Instance is null || TextureManifest.Instance.Entries.Count == 0)
            throw new InvalidOperationException($"Load {TextureManifest.ManifestName} first before using the texture cache manager.");

        if (TextureFileCache.Instance.Entry is null)
            throw new InvalidOperationException("Resolve a texture entry before using the texture cache manager.");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "texture" : value;
    }
}

public sealed class MipmapService
{
    public DdsFile Regenerate(TexturePreviewTexture texture, int mipCount)
    {
        return DdsService.BuildDds(texture.Width, texture.Height, texture.RgbaPixels, DdsService.ResolveFormat(texture), mipCount);
    }

    public TexturePreviewTexture RegeneratePreview(TexturePreviewTexture texture, int mipCount)
    {
        DdsFile dds = Regenerate(texture, mipCount);
        using MemoryStream stream = new();
        dds.Save(stream, new DdsSaveConfig(DdsService.ResolveFormat(texture), 0, 0, false, false));
        stream.Position = 0;

        DdsFile rebuilt = new();
        rebuilt.Load(stream);

        return new TexturePreviewTexture
        {
            Name = texture.Name,
            SourcePath = texture.SourcePath,
            SourceDescription = texture.SourceDescription,
            ExportPath = texture.ExportPath,
            Bitmap = BitmapSourceToBitmap(rebuilt.BitmapSource),
            RgbaPixels = rebuilt.BitmapData,
            Width = rebuilt.Width,
            Height = rebuilt.Height,
            MipCount = rebuilt.MipMaps.Count,
            SelectedMipIndex = 0,
            Format = rebuilt.FileFormat.ToString(),
            Compression = rebuilt.FileFormat.ToString(),
            ContainerType = "DDS",
            MipSource = "Generated",
            Slot = texture.Slot,
            ContainerBytes = null,
            AvailableMipLevels = Array.Empty<TexturePreviewMipLevel>()
        };
    }

    private static System.Drawing.Bitmap BitmapSourceToBitmap(System.Windows.Media.Imaging.BitmapSource bitmapSource)
    {
        using MemoryStream outStream = new();
        System.Windows.Media.Imaging.BitmapEncoder encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
        encoder.Save(outStream);
        outStream.Position = 0;
        using System.Drawing.Bitmap temp = new(outStream);
        return new System.Drawing.Bitmap(temp);
    }
}

public sealed class UpscaleService
{
    private readonly TextureUpscaleService _upscaleService = new();

    public IReadOnlyList<string> ProviderNames => _upscaleService.ProviderNames;

    public TexturePreviewTexture Upscale(TexturePreviewTexture source, int targetSize, string? providerName = null)
    {
        return _upscaleService.Upscale(source, targetSize, providerName);
    }
}

public sealed class SafeInjectService
{
    private readonly TexturePreviewInjector _injector = new();

    public async Task InjectAsync(string upkPath, string exportPath, TexturePreviewTexture sourceTexture, Action<string>? log = null)
    {
        Validate(sourceTexture);
        await _injector.InjectAsync(upkPath, exportPath, sourceTexture, log).ConfigureAwait(true);
    }

    public Task<TextureInjectionTargetInfo> ResolveTargetInfoAsync(string upkPath, string exportPath)
    {
        return _injector.ResolveTargetInfoAsync(upkPath, exportPath);
    }

    public bool CanInject(TexturePreviewTexture sourceTexture, out string reason)
    {
        try
        {
            Validate(sourceTexture);
            reason = "Texture passed validation.";
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public async Task<(bool CanInject, string Reason)> ValidateTargetAsync(string upkPath, string exportPath, TexturePreviewTexture sourceTexture, Action<string>? log = null)
    {
        Validate(sourceTexture);

        TextureInjectionTargetInfo target = await ResolveTargetInfoAsync(upkPath, exportPath).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(target.ManifestFilePath) || !File.Exists(target.ManifestFilePath))
            return (false, "Texture manifest is not loaded or the manifest file is missing.");

        if (string.IsNullOrWhiteSpace(target.SourceTextureCachePath) || !File.Exists(target.SourceTextureCachePath))
            return (false, $"Source texture cache is missing: {target.SourceTextureCachePath}");

        if (string.IsNullOrWhiteSpace(target.DestinationTextureCachePath))
            return (false, "Destination texture cache path could not be resolved.");

        string targetFormatLabel = DdsService.SuggestFormatLabel(ResolveTextureTypeHint(sourceTexture), HasAlpha(sourceTexture), sourceTexture.Name);
        log?.Invoke($"Target format recommendation: {targetFormatLabel}");
        log?.Invoke($"Target manifest: {target.ManifestFilePath}");
        log?.Invoke($"Source cache: {target.SourceTextureCachePath}");
        log?.Invoke($"Destination cache: {target.DestinationTextureCachePath}");
        log?.Invoke($"Source texture: {sourceTexture.Width}x{sourceTexture.Height}, {sourceTexture.Format}, {sourceTexture.ContainerType}");
        log?.Invoke($"Alpha present: {HasAlpha(sourceTexture)}");
        return (true, "Target manifest and cache paths resolved.");
    }

    private static void Validate(TexturePreviewTexture sourceTexture)
    {
        ArgumentNullException.ThrowIfNull(sourceTexture);
        if (sourceTexture.Width <= 0 || sourceTexture.Height <= 0)
            throw new InvalidOperationException("Texture has invalid dimensions.");

        if (sourceTexture.RgbaPixels is null || sourceTexture.RgbaPixels.Length != sourceTexture.Width * sourceTexture.Height * 4)
            throw new InvalidOperationException("Texture does not contain a valid RGBA buffer.");
    }

    private static TextureType ResolveTextureTypeHint(TexturePreviewTexture sourceTexture)
    {
        string name = (sourceTexture.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("normal") || name.Contains("_n") || name.Contains("_nm") || name.Contains("nrm"))
            return TextureType.Normal;
        if (name.Contains("spec") || name.Contains("gloss") || name.Contains("refl"))
            return TextureType.Specular;
        if (name.Contains("mask") || name.Contains("alpha") || name.Contains("occlusion"))
            return TextureType.Mask;
        if (name.Contains("emit") || name.Contains("glow") || name.Contains("illum"))
            return TextureType.Emissive;
        return TextureType.Diffuse;
    }

    private static bool HasAlpha(TexturePreviewTexture sourceTexture)
    {
        if (sourceTexture.RgbaPixels is null || sourceTexture.RgbaPixels.Length < 4)
            return false;

        for (int i = 3; i < sourceTexture.RgbaPixels.Length; i += 4)
        {
            if (sourceTexture.RgbaPixels[i] < 255)
                return true;
        }

        return false;
    }
}

