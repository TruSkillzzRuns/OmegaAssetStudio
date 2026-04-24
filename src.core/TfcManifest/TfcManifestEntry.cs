using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OmegaAssetStudio.TfcManifest;

public sealed class TfcManifestEntry : INotifyPropertyChanged
{
    private string packageName = string.Empty;
    private string textureName = string.Empty;
    private string tfcFileName = string.Empty;
    private int chunkIndex;
    private long offset;
    private long size;
    private Guid textureGuid = Guid.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PackageName
    {
        get => packageName;
        set => SetField(ref packageName, value);
    }

    public string TextureName
    {
        get => textureName;
        set => SetField(ref textureName, value);
    }

    public string TfcFileName
    {
        get => tfcFileName;
        set => SetField(ref tfcFileName, value);
    }

    public int ChunkIndex
    {
        get => chunkIndex;
        set => SetField(ref chunkIndex, value);
    }

    public long Offset
    {
        get => offset;
        set => SetField(ref offset, value);
    }

    public long Size
    {
        get => size;
        set => SetField(ref size, value);
    }

    public Guid TextureGuid
    {
        get => textureGuid;
        set => SetField(ref textureGuid, value);
    }

    public List<TfcManifestChunk> Chunks { get; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(PackageName)
        ? TextureName
        : $"{PackageName}.{TextureName}";

    public void Normalize()
    {
        if (Chunks.Count == 0)
        {
            Chunks.Add(new TfcManifestChunk
            {
                ChunkIndex = ChunkIndex,
                Offset = Offset,
                Size = Size
            });
        }
        else
        {
            TfcManifestChunk first = Chunks[0];
            ChunkIndex = first.ChunkIndex;
            Offset = first.Offset;
            Size = first.Size;
        }

        OnPropertyChanged(nameof(DisplayName));
    }

    public TfcManifestEntry Clone()
    {
        TfcManifestEntry clone = new()
        {
            PackageName = PackageName,
            TextureName = TextureName,
            TfcFileName = TfcFileName,
            ChunkIndex = ChunkIndex,
            Offset = Offset,
            Size = Size,
            TextureGuid = TextureGuid
        };

        foreach (TfcManifestChunk chunk in Chunks)
        {
            clone.Chunks.Add(new TfcManifestChunk
            {
                ChunkIndex = chunk.ChunkIndex,
                Offset = chunk.Offset,
                Size = chunk.Size
            });
        }

        return clone;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(PackageName) or nameof(TextureName))
            OnPropertyChanged(nameof(DisplayName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TfcManifestChunk
{
    public int ChunkIndex { get; set; }
    public long Offset { get; set; }
    public long Size { get; set; }
}

