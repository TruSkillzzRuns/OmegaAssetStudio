using System.Text;

namespace OmegaAssetStudio.TfcManifest;

public sealed class TfcManifestReader
{
    public TfcManifestDocument Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("TextureFileCacheManifest.bin was not found.", path);

        TfcManifestDocument document = new()
        {
            SourceDirectory = Path.GetDirectoryName(path)
        };

        using BinaryReader reader = new(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        uint entryCount = reader.ReadUInt32();

        for (uint i = 0; i < entryCount; i++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                break;

            string rawTextureName = ReadString(reader);
            Guid textureGuid = new(reader.ReadBytes(16));
            string tfcFileName = ReadString(reader);
            uint mipCount = reader.ReadUInt32();

            TfcManifestEntry entry = new()
            {
                TextureGuid = textureGuid,
                TfcFileName = tfcFileName,
                TextureName = ExtractTextureName(rawTextureName),
                PackageName = ExtractPackageName(rawTextureName)
            };

            for (uint mip = 0; mip < mipCount; mip++)
            {
                uint chunkIndex = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                entry.Chunks.Add(new TfcManifestChunk
                {
                    ChunkIndex = unchecked((int)chunkIndex),
                    Offset = offset,
                    Size = size
                });
            }

            entry.Normalize();
            document.Entries.Add(entry);
        }

        return document;
    }

    private static string ExtractPackageName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        int lastDot = value.LastIndexOf('.');
        if (lastDot <= 0)
            return string.Empty;

        return value[..lastDot];
    }

    private static string ExtractTextureName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        int lastDot = value.LastIndexOf('.');
        if (lastDot < 0 || lastDot + 1 >= value.Length)
            return value;

        return value[(lastDot + 1)..];
    }

    private static string ReadString(BinaryReader reader)
    {
        uint length = reader.ReadUInt32();
        byte[] bytes = reader.ReadBytes((int)length);
        int nullIndex = Array.IndexOf(bytes, (byte)0);
        if (nullIndex >= 0)
            bytes = bytes[..nullIndex];

        return Encoding.UTF8.GetString(bytes);
    }
}

