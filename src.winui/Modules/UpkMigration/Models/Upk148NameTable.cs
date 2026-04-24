using System.Collections.Generic;
using System.Linq;
using UpkManager.Models.UpkFile.Tables;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class Upk148NameTable
{
    private readonly List<Upk148NameTableEntry> entries;

    public Upk148NameTable(IEnumerable<Upk148NameTableEntry> entries)
    {
        this.entries = entries.ToList();
    }

    public IReadOnlyList<Upk148NameTableEntry> Entries => entries;

    public IReadOnlyList<string> Names => entries.Select(entry => entry.Name).ToList();
}

public sealed class Upk148NameTableEntry
{
    public Upk148NameTableEntry(UnrealNameTableEntry entry)
    {
        Index = entry.TableIndex;
        Name = entry.Name.String ?? string.Empty;
        Flags = (uint)entry.Flags;
    }

    public int Index { get; }

    public string Name { get; }

    public uint Flags { get; }
}

