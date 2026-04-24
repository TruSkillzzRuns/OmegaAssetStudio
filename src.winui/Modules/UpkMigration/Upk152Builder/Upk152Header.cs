namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk152Builder;

public sealed class Upk152Header
{
    public string SourcePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public ushort SourceVersion { get; set; }
    public ushort TargetVersion { get; set; } = 152;
    public ushort Licensee { get; set; }
    public uint Flags { get; set; }
    public uint EngineVersion { get; set; }
    public uint CookerVersion { get; set; }
    public int ImportCount { get; set; }
    public int ExportCount { get; set; }
    public int NameCount { get; set; }
}

