using System.ComponentModel;
using System.Runtime.CompilerServices;
using OmegaAssetStudio.TexturePreview;

namespace OmegaAssetStudio.WinUI.Models;

public enum TextureType
{
    Diffuse,
    Normal,
    Specular,
    Emissive,
    Mask,
    UI,
    FX
}

public enum TextureOperation
{
    Load,
    Replace,
    ReplaceInlineMip,
    Extract,
    Upscale,
    RegenerateMipmaps,
    Export,
    Inject,
    RebuildCache,
    DefragmentCache,
    Undo,
    Redo,
    Reset,
    BatchExport,
    BatchInject
}

public sealed record MipInfo(
    int AbsoluteIndex,
    int RelativeIndex,
    int Width,
    int Height,
    int DataSize,
    string Source,
    string Format);

public sealed class Texture2DInfo
{
    public string Name { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string ExportPath { get; init; } = string.Empty;
    public string SourceDescription { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Compression { get; init; } = string.Empty;
    public string ContainerType { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int MipCount { get; init; }
    public int SelectedMipIndex { get; init; }
    public string MipSource { get; init; } = string.Empty;
    public TextureType TextureType { get; init; } = TextureType.Diffuse;
    public IReadOnlyList<MipInfo> Mips { get; init; } = Array.Empty<MipInfo>();
}

public sealed class TextureHistoryEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public TextureOperation Operation { get; init; }
    public string Notes { get; init; } = string.Empty;
    public Texture2DInfo? Before { get; init; }
    public Texture2DInfo? After { get; init; }

    public override string ToString()
    {
        string before = Before is null ? "(none)" : Before.Name;
        string after = After is null ? "(none)" : After.Name;
        return $"[{Timestamp:HH:mm:ss}] {Operation}: {before} -> {after} {Notes}".TrimEnd();
    }
}

public sealed class TextureItemViewModel : INotifyPropertyChanged
{
    private string displayName = string.Empty;
    private string sourcePath = string.Empty;
    private string exportPath = string.Empty;
    private string sourceKind = string.Empty;
    private string format = string.Empty;
    private string sizeText = string.Empty;
    private string slotText = string.Empty;
    private bool isLoaded;
    private TexturePreviewTexture? texture;
    private TexturePreviewTexture? originalTexture;
    private TextureType textureType;
    private string pendingReplacementPath = string.Empty;
    private string pendingReplacementFormat = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName
    {
        get => displayName;
        set => SetField(ref displayName, value);
    }

    public string SourcePath
    {
        get => sourcePath;
        set => SetField(ref sourcePath, value);
    }

    public string ExportPath
    {
        get => exportPath;
        set => SetField(ref exportPath, value);
    }

    public string SourceKind
    {
        get => sourceKind;
        set => SetField(ref sourceKind, value);
    }

    public string Format
    {
        get => format;
        set => SetField(ref format, value);
    }

    public string SizeText
    {
        get => sizeText;
        set => SetField(ref sizeText, value);
    }

    public string SlotText
    {
        get => slotText;
        set => SetField(ref slotText, value);
    }

    public bool IsLoaded
    {
        get => isLoaded;
        set => SetField(ref isLoaded, value);
    }

    public TextureType TextureType
    {
        get => textureType;
        set => SetField(ref textureType, value);
    }

    public TexturePreviewTexture? Texture
    {
        get => texture;
        set => SetField(ref texture, value);
    }

    public TexturePreviewTexture? OriginalTexture
    {
        get => originalTexture;
        set => SetField(ref originalTexture, value);
    }

    public string PendingReplacementPath
    {
        get => pendingReplacementPath;
        set => SetField(ref pendingReplacementPath, value);
    }

    public string PendingReplacementFormat
    {
        get => pendingReplacementFormat;
        set => SetField(ref pendingReplacementFormat, value);
    }

    public string SearchKey => $"{DisplayName} {SourcePath} {ExportPath} {SourceKind} {Format} {SizeText} {SlotText} {PendingReplacementPath} {PendingReplacementFormat}";

    public override string ToString() => DisplayName;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

