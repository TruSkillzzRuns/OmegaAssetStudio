using System.Collections.Generic;
using UpkManager.Models.UpkFile;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class Upk148Header
{
    public Upk148Header(string sourcePath, UnrealHeader rawHeader, Upk148NameTable nameTable, Upk148ExportTable exportTable)
    {
        SourcePath = sourcePath;
        RawHeader = rawHeader;
        NameTable = nameTable;
        ExportTable = exportTable;
    }

    public string SourcePath { get; }

    public UnrealHeader RawHeader { get; }

    public Upk148NameTable NameTable { get; }

    public Upk148ExportTable ExportTable { get; }

    public ushort Version => RawHeader.Version;

    public ushort Licensee => RawHeader.Licensee;

    public uint Flags => RawHeader.Flags;

    public int ImportCount => RawHeader.ImportTable.Count;

    public int ExportCount => RawHeader.ExportTable.Count;

    public IReadOnlyList<string> NameStrings => NameTable.Names;
}

