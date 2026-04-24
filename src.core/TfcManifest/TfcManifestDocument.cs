namespace OmegaAssetStudio.TfcManifest;

public sealed class TfcManifestDocument
{
    public List<TfcManifestEntry> Entries { get; } = [];

    public int Version { get; set; } = 1;

    public byte[]? RawHeader { get; set; }

    public string? SourceDirectory { get; set; }
}

