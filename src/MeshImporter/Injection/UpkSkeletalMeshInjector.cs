using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;

namespace OmegaAssetStudio.MeshImporter;

internal sealed class UpkSkeletalMeshInjector
{
    private readonly UE3LodSerializer _serializer = new();

    public async Task InjectAsync(
        string upkPath,
        UnrealExportTableEntry targetExport,
        MeshImportContext context,
        UE3LodModel lodModel,
        string outputUpkPath)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(upkPath).ConfigureAwait(false);
        UnrealHeader header = context.Header;

        List<UpkRepacker.ExportBuffer> exportBuffers = header.ExportTable
            .Select(static export => new UpkRepacker.ExportBuffer(export.UnrealObjectReader.GetBytes(), []))
            .ToList();
        exportBuffers[targetExport.TableIndex - 1] = BuildReplacementExportBuffer(context, lodModel);

        byte[] repacked = header.CompressedChunks.Count > 0
            ? UpkRepacker.RepackCompressed(originalBytes, header, exportBuffers)
            : UpkRepacker.Repack(originalBytes, header, exportBuffers);

        await File.WriteAllBytesAsync(outputUpkPath, repacked).ConfigureAwait(false);
    }

    private UpkRepacker.ExportBuffer BuildReplacementExportBuffer(MeshImportContext context, UE3LodModel lodModel)
    {
        SerializedLodModel serializedLod = _serializer.Serialize(lodModel, context);
        byte[] newLodBytes = serializedLod.Bytes;

        int newSectionCount = lodModel.Inner.Sections.Count;
        int originalSectionCount = context.OriginalLod.Sections.Count;

        byte[] prefix = context.RawExportData[..context.LodDataOffset];
        if (newSectionCount > originalSectionCount)
        {
            prefix = ExpandLodInfoForNewSections(
                prefix,
                originalSectionCount,
                newSectionCount,
                context);
        }

        int suffixOffset = context.LodDataOffset + context.LodDataSize;
        int suffixLength = context.RawExportData.Length - suffixOffset;

        byte[] output = new byte[prefix.Length + newLodBytes.Length + suffixLength];
        Buffer.BlockCopy(prefix, 0, output, 0, prefix.Length);
        Buffer.BlockCopy(newLodBytes, 0, output, prefix.Length, newLodBytes.Length);
        Buffer.BlockCopy(context.RawExportData, suffixOffset, output, prefix.Length + newLodBytes.Length, suffixLength);

        IReadOnlyList<UpkRepacker.BulkDataPatch> patches = serializedLod.BulkDataPatches
            .Select(p => new UpkRepacker.BulkDataPatch(
                prefix.Length + p.OffsetFieldPosition,
                prefix.Length + p.DataStartPosition))
            .ToArray();

        return new UpkRepacker.ExportBuffer(output, patches);
    }

    private static byte[] ExpandLodInfoForNewSections(
        byte[] prefix,
        int originalSectionCount,
        int newSectionCount,
        MeshImportContext context)
    {
        int addedSections = newSectionCount - originalSectionCount;
        if (addedSections <= 0)
            return prefix;

        if (!FindLodInfoPositions(
                prefix,
                context,
                out int lodInfoDsPos,
                out int shadowDsPos,
                out int shadowCountPos,
                out int shadowDataEnd,
                out int sortDsPos,
                out int sortCountPos,
                out int sortDataStart,
                out int sortDataEnd,
                out int sortEntrySize))
        {
            return prefix;
        }

        int extraBytes = addedSections * (1 + sortEntrySize);
        using MemoryStream ms = new(prefix.Length + extraBytes);

        ms.Write(prefix, 0, shadowDataEnd);
        for (int i = 0; i < addedSections; i++)
            ms.WriteByte(1);

        ms.Write(prefix, shadowDataEnd, sortDataEnd - shadowDataEnd);
        if (sortEntrySize > 0 && sortDataStart + sortEntrySize <= prefix.Length)
        {
            for (int i = 0; i < addedSections; i++)
                ms.Write(prefix, sortDataStart, sortEntrySize);
        }

        ms.Write(prefix, sortDataEnd, prefix.Length - sortDataEnd);

        byte[] result = ms.ToArray();
        int shift = addedSections;

        PatchInt32Add(result, lodInfoDsPos, extraBytes);
        PatchInt32Add(result, shadowDsPos, addedSections);
        PatchInt32Add(result, shadowCountPos, addedSections);
        PatchInt32Add(result, sortDsPos + shift, addedSections * sortEntrySize);
        PatchInt32Add(result, sortCountPos + shift, addedSections);

        return result;
    }

    private static bool FindLodInfoPositions(
        byte[] data,
        MeshImportContext context,
        out int lodInfoDsPos,
        out int shadowDsPos,
        out int shadowCountPos,
        out int shadowDataEnd,
        out int sortDsPos,
        out int sortCountPos,
        out int sortDataStart,
        out int sortDataEnd,
        out int sortEntrySize)
    {
        lodInfoDsPos = shadowDsPos = shadowCountPos = shadowDataEnd = 0;
        sortDsPos = sortCountPos = sortDataStart = sortDataEnd = sortEntrySize = 0;

        int idxNone = -1;
        int idxArrayProp = -1;
        int idxBoolProp = -1;
        int idxLodInfo = -1;
        int idxShadow = -1;
        int idxSort = -1;

        for (int i = 0; i < context.Header.NameTable.Count; i++)
        {
            string entryName = context.Header.NameTable[i].Name.String ?? string.Empty;
            switch (entryName.ToLowerInvariant())
            {
                case "none":
                    idxNone = i;
                    break;
                case "arrayproperty":
                    idxArrayProp = i;
                    break;
                case "boolproperty":
                    idxBoolProp = i;
                    break;
                case "lodinfo":
                    idxLodInfo = i;
                    break;
                case "benableshadowcasting":
                    idxShadow = i;
                    break;
                case "trianglesortsettings":
                    idxSort = i;
                    break;
            }
        }

        if (idxLodInfo < 0 || idxArrayProp < 0 || idxShadow < 0 || idxSort < 0 || idxNone < 0)
            return false;

        int o = 4;
        while (o < data.Length - 24)
        {
            int ni = BitConverter.ToInt32(data, o);
            if (ni == idxNone)
                break;

            o += 8;

            int ti = BitConverter.ToInt32(data, o);
            o += 8;

            int dsPos = o;
            int ds = BitConverter.ToInt32(data, o);
            o += 4;
            o += 4;

            if (ni == idxLodInfo && ti == idxArrayProp)
            {
                lodInfoDsPos = dsPos;
                int lodDataStart = o;
                int lodCount = BitConverter.ToInt32(data, o);
                o += 4;
                if (lodCount < 1)
                    return false;

                while (o < lodDataStart + ds)
                {
                    int pni = BitConverter.ToInt32(data, o);
                    if (pni == idxNone)
                    {
                        o += 8;
                        break;
                    }

                    o += 8;

                    int pti = BitConverter.ToInt32(data, o);
                    o += 8;

                    int pDsPos = o;
                    int pds = BitConverter.ToInt32(data, o);
                    o += 4;
                    o += 4;

                    if (pni == idxShadow && pti == idxArrayProp)
                    {
                        shadowDsPos = pDsPos;
                        shadowCountPos = o;
                        shadowDataEnd = o + pds;
                        o += pds;
                    }
                    else if (pni == idxSort && pti == idxArrayProp)
                    {
                        sortDsPos = pDsPos;
                        sortCountPos = o;
                        int sortCount = BitConverter.ToInt32(data, o);
                        sortDataStart = o + 4;
                        sortDataEnd = o + pds;
                        sortEntrySize = sortCount > 0 ? (pds - 4) / sortCount : 0;
                        o += pds;
                    }
                    else if (pti == idxBoolProp)
                    {
                        o += 1;
                    }
                    else
                    {
                        o += pds;
                    }
                }

                return shadowDsPos > 0 && sortDsPos > 0 && sortEntrySize > 0;
            }

            if (ti == idxBoolProp)
                o += 1;
            else
                o += ds;
        }

        return false;
    }

    private static void PatchInt32Add(byte[] data, int offset, int addValue)
    {
        int current = BitConverter.ToInt32(data, offset);
        BitConverter.TryWriteBytes(data.AsSpan(offset), current + addValue);
    }
}

