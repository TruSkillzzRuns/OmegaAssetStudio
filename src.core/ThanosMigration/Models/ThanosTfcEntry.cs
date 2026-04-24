using System;

namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosTfcEntry
{
    public string PackageName { get; set; } = string.Empty;

    public string TextureName { get; set; } = string.Empty;

    public Guid TextureGuid { get; set; }

    public string TfcFileName { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public long Offset { get; set; }

    public long Size { get; set; }
}

