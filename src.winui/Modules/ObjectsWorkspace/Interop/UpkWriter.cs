using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Interop;

public sealed class UpkWriter
{
    private readonly UpkFileRepository repository = new();

    public void Write(UpkPackage package, string outputPath)
    {
        WriteAsync(package, outputPath).GetAwaiter().GetResult();
    }

    public async Task WriteAsync(UpkPackage package, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (package.SourceHeader == null)
            throw new InvalidOperationException("Package source header is missing.");

        SyncPackageToHeader(package);

        package.SourceHeader.Repository ??= repository;
        await package.SourceHeader.Repository.SaveUpkFile(package.SourceHeader, outputPath).ConfigureAwait(false);
    }

    private static void SyncPackageToHeader(UpkPackage package)
    {
        if (package.SourceHeader == null)
            return;

        SyncExports(package.SourceHeader, package.Exports);
    }

    private static void SyncExports(UnrealHeader header, IReadOnlyList<UpkExportEntry> exports)
    {
        Dictionary<int, UpkExportEntry> exportMap = exports.ToDictionary(static export => export.TableIndex);

        foreach (UnrealExportTableEntry headerExport in header.ExportTable)
        {
            if (!exportMap.TryGetValue(headerExport.TableIndex, out UpkExportEntry? packageExport))
                continue;

            SetProperty(headerExport, "UnrealObject", new RawUnrealObject(packageExport.RawData));
            SetProperty(headerExport, "SerialDataSize", packageExport.RawData.Length);
            SetProperty(headerExport, "SerialDataOffset", packageExport.SerialOffset);
        }
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property?.SetValue(target, value);
    }
}
