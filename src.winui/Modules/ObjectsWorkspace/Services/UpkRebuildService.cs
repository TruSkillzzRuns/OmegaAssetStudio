using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Interop;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Services;

public sealed class UpkRebuildService
{
    private readonly UpkWriter writer = new();

    public void ApplyHexEditToExport(UpkPackage package, UpkExportEntry export, byte[] newRawData)
    {
        export.RawData = newRawData;
        export.SerialSize = newRawData.Length;
    }

    public void RecalculateExportOffsets(UpkPackage package)
    {
        int currentOffset = package.Exports.Count > 0
            ? package.Exports.OrderBy(static export => export.TableIndex).First().SerialOffset
            : package.Summary.ExportOffset;

        foreach (var export in package.Exports.OrderBy(static export => export.TableIndex))
        {
            export.SerialOffset = currentOffset;
            currentOffset += export.SerialSize;
        }
    }

    public void RecalculateSummary(UpkPackage package)
    {
        package.Summary.NameCount = package.Names.Count;
        package.Summary.ImportCount = package.Imports.Count;
        package.Summary.ExportCount = package.Exports.Count;
    }

    public void RebuildAndSavePackage(UpkPackage package, string outputPath)
    {
        writer.Write(package, outputPath);
    }
}
