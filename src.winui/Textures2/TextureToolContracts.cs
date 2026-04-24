using System.Collections.ObjectModel;

namespace OmegaAssetStudio.WinUI.Textures2;

public sealed class UpkMetadata
{
    public string FilePath { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public int FileSize { get; init; }
    public bool IsCompressed { get; init; }
    public int NameTableCount { get; init; }
    public int ImportTableCount { get; init; }
    public int ExportTableCount { get; init; }
    public IReadOnlyList<string> NameTable { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Imports { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ExportInfo> Exports { get; init; } = Array.Empty<ExportInfo>();
}

public sealed record ExportInfo(
    int TableIndex,
    string ObjectName,
    string PathName,
    string ClassName,
    string SuperClassName,
    string OuterPathName,
    int SerialOffset,
    int SerialSize,
    uint PackageFlags,
    bool IsTexture2D);


