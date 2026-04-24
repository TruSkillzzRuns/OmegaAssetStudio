using System;
using System.Linq;
using System.Threading.Tasks;

using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Interop;

public sealed class UpkReader
{
    private readonly UpkFileRepository repository = new();

    public UpkPackage Read(string path)
    {
        return ReadAsync(path).GetAwaiter().GetResult();
    }

    public async Task<UpkPackage> ReadAsync(string path)
    {
        UnrealHeader header = await repository.LoadUpkFile(path).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        var package = new UpkPackage
        {
            OriginalPath = path,
            SourceHeader = header
        };

        PopulateSummary(header, package);
        PopulateNames(header, package);
        PopulateImports(header, package);
        PopulateExports(header, package);

        return package;
    }

    private static void PopulateSummary(UnrealHeader header, UpkPackage package)
    {
        package.Summary.NameCount = header.NameTableCount;
        package.Summary.NameOffset = header.NameTableOffset;
        package.Summary.ImportCount = header.ImportTableCount;
        package.Summary.ImportOffset = header.ImportTableOffset;
        package.Summary.ExportCount = header.ExportTableCount;
        package.Summary.ExportOffset = header.ExportTableOffset;
        package.Summary.FileSize = (int)header.FileSize;

        package.Summary.Generations.Clear();
        foreach (var generation in header.GenerationTable)
        {
            package.Summary.Generations.Add(new UpkGenerationInfo
            {
                ExportCount = generation.ExportTableCount,
                NetObjectCount = generation.NetObjectCount
            });
        }
    }

    private static void PopulateNames(UnrealHeader header, UpkPackage package)
    {
        package.Names.Clear();

        foreach (UnrealNameTableEntry entry in header.NameTable.OrderBy(static entry => entry.TableIndex))
        {
            package.Names.Add(new UpkNameEntry
            {
                TableIndex = entry.TableIndex,
                Name = entry.Name.String ?? string.Empty
            });
        }
    }

    private static void PopulateImports(UnrealHeader header, UpkPackage package)
    {
        package.Imports.Clear();

        foreach (UnrealImportTableEntry entry in header.ImportTable.OrderBy(static entry => entry.TableIndex))
        {
            package.Imports.Add(new UpkImportEntry
            {
                TableIndex = entry.TableIndex,
                ObjectName = entry.GetPathName(),
                ClassName = entry.ClassNameIndex?.Name ?? string.Empty,
                OuterName = entry.OuterReferenceNameIndex?.Name ?? string.Empty
            });
        }
    }

    private static void PopulateExports(UnrealHeader header, UpkPackage package)
    {
        package.Exports.Clear();

        foreach (UnrealExportTableEntry entry in header.ExportTable.OrderBy(static entry => entry.TableIndex))
        {
            package.Exports.Add(new UpkExportEntry
            {
                TableIndex = entry.TableIndex,
                ObjectName = entry.GetPathName(),
                ClassName = entry.ClassReferenceNameIndex?.Name ?? string.Empty,
                OuterName = entry.OuterReferenceNameIndex?.Name ?? string.Empty,
                SerialSize = entry.SerialDataSize,
                SerialOffset = entry.SerialDataOffset,
                RawData = entry.UnrealObjectReader?.GetBytes() ?? [],
                IsExport = true,
                IsImport = false
            });
        }
    }
}
