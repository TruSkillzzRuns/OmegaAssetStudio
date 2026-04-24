using System.IO;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;
using UpkManager.Models.UpkFile.Engine.Texture;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;

public sealed class TextureConverter148ToNeutral
{
    public IReadOnlyList<NeutralTexture> Convert(Upk148ExportTableEntry entry, Action<string>? log = null)
    {
        if (!string.Equals(entry.ClassName, "Texture2D", StringComparison.OrdinalIgnoreCase) ||
            !UpkExportHydrator.TryHydrate(entry, out UTexture2D? texture, log, false) ||
            texture is null)
            return [];

        NeutralTexture neutral = new()
        {
            Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
            Width = texture.SizeX,
            Height = texture.SizeY,
            Format = texture.Format.ToString(),
            SourcePath = entry.PathName
        };

        try
        {
            using Stream? stream = texture.GetObjectStream();
            if (stream is not null)
            {
                using MemoryStream memory = new();
                stream.CopyTo(memory);
                neutral.PixelData = memory.ToArray();
                log?.Invoke($"Converted texture {entry.PathName} with {neutral.PixelData.Length} byte(s) of pixel data.");
            }
            else
            {
                neutral.Notes.Add("Texture has no readable object stream.");
                log?.Invoke($"Converted texture {entry.PathName} without readable pixel data.");
            }
        }
        catch (Exception ex)
        {
            neutral.Notes.Add(ex.Message);
            log?.Invoke($"Warning: texture {entry.PathName} could not be streamed: {ex.Message}");
        }

        return [neutral];
    }
}

