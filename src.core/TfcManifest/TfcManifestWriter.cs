using System.Text;

namespace OmegaAssetStudio.TfcManifest;

public sealed class TfcManifestWriter
{
    public void Write(string path, TfcManifestDocument document)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using BinaryWriter writer = new(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None));
        writer.Write((uint)document.Entries.Count);

        foreach (TfcManifestEntry entry in document.Entries)
        {
            string textureKey = ComposeTextureKey(entry);
            WriteString(writer, textureKey);
            writer.Write(entry.TextureGuid.ToByteArray());
            WriteString(writer, entry.TfcFileName ?? string.Empty);

            IReadOnlyList<TfcManifestChunk> chunks = entry.Chunks.Count > 0
                ? entry.Chunks
                : [new TfcManifestChunk
                {
                    ChunkIndex = entry.ChunkIndex,
                    Offset = entry.Offset,
                    Size = entry.Size
                }];

            writer.Write((uint)chunks.Count);
            foreach (TfcManifestChunk chunk in chunks)
            {
                writer.Write((uint)Math.Max(0, chunk.ChunkIndex));
                writer.Write((uint)Math.Max(0, chunk.Offset));
                writer.Write((uint)Math.Max(0, chunk.Size));
            }
        }
    }

    private static string ComposeTextureKey(TfcManifestEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.PackageName) && !string.IsNullOrWhiteSpace(entry.TextureName))
            return $"{entry.PackageName}.{entry.TextureName}";

        return entry.TextureName ?? string.Empty;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes((value ?? string.Empty) + '\0');
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }
}

