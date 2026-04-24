using System.Collections.Generic;
using System.Linq;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Objects;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class Upk148ExportTable
{
    private readonly List<Upk148ExportTableEntry> entries;

    public Upk148ExportTable(IEnumerable<Upk148ExportTableEntry> entries)
    {
        this.entries = entries.ToList();
    }

    public IReadOnlyList<Upk148ExportTableEntry> Entries => entries;

    public IEnumerable<Upk148ExportTableEntry> SkeletalMeshes => entries.Where(entry => entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Mesh.USkeletalMesh);

    public IEnumerable<Upk148ExportTableEntry> StaticMeshes => entries.Where(entry => entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Mesh.UStaticMesh);

    public IEnumerable<Upk148ExportTableEntry> Textures => entries.Where(entry => entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Texture.UTexture2D);

    public IEnumerable<Upk148ExportTableEntry> Animations => entries.Where(entry => entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Anim.UAnimSequence || entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Anim.UAnimSet);

    public IEnumerable<Upk148ExportTableEntry> Materials => entries.Where(entry => entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Material.UMaterial || entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Material.UMaterialInstance || entry.ResolvedObject is UpkManager.Models.UpkFile.Engine.Material.UMaterialInstanceConstant);
}

public sealed class Upk148ExportTableEntry
{
    public Upk148ExportTableEntry(UnrealExportTableEntry rawExport)
    {
        RawExport = rawExport;
        ExportIndex = rawExport.TableIndex;
        PathName = rawExport.GetPathName();
        ClassName = rawExport.ClassReferenceNameIndex?.Name ?? string.Empty;
        ObjectName = rawExport.ObjectNameIndex?.Name ?? string.Empty;
        ResolvedObject = ExtractResolvedObject(rawExport);
        ResolvedType = ResolvedObject?.GetType().Name ?? ClassName;
    }

    public UnrealExportTableEntry RawExport { get; }

    public int ExportIndex { get; }

    public string PathName { get; }

    public string ClassName { get; }

    public string ObjectName { get; }

    public string ResolvedType { get; }

    public object? ResolvedObject { get; }

    public int SerialSize => RawExport.SerialDataSize;

    private static object? ExtractResolvedObject(UnrealExportTableEntry rawExport)
    {
        try
        {
            if (rawExport.UnrealObject is IUnrealObject unrealObject)
                return unrealObject.UObject;
        }
        catch
        {
        }

        return null;
    }
}

