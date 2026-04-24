using System.Collections.Generic;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;

public sealed class NeutralTexture
{
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public byte[] PixelData { get; set; } = [];
    public string SourcePath { get; set; } = string.Empty;
    public List<string> Notes { get; } = [];
}

