using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration;
using UpkManager.Models.UpkFile;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;

public sealed class Upk148Reader
{
    private readonly UpkFileRepository repository = new();

    public async Task<Upk148Document> ReadAsync(string upkPath, Action<string>? log = null, UpkMigrationReadOnlyContext? readOnlyContext = null)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new InvalidOperationException("A source UPK path is required.");

        if (!File.Exists(upkPath))
            throw new FileNotFoundException("The selected UPK could not be found.", upkPath);

        readOnlyContext?.EnsureReadableSource(upkPath);

        log?.Invoke($"Reading source UPK: {Path.GetFileName(upkPath)}");
        UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);
        log?.Invoke($"Header parsed: Version {header.Version}, {header.ExportTable.Count} exports, {header.NameTable.Count} names.");

        return new Upk148Document(
            upkPath,
            header,
            new Upk148Header(
                upkPath,
                header,
                new Upk148NameTable(header.NameTable.Select(entry => new Upk148NameTableEntry(entry))),
                new Upk148ExportTable(header.ExportTable.Select(entry => new Upk148ExportTableEntry(entry)))));
    }
}

public sealed class Upk148Document
{
    public Upk148Document(string sourcePath, UnrealHeader rawHeader, Upk148Header header)
    {
        SourcePath = sourcePath;
        RawHeader = rawHeader;
        Header = header;
    }

    public string SourcePath { get; }

    public UnrealHeader RawHeader { get; }

    public Upk148Header Header { get; }
}

