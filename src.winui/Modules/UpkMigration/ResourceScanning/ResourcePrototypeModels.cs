using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.ResourceScanning;

public sealed class ResourcePrototypeCell
{
    public string ClientMap { get; set; } = string.Empty;

    public Vector3 BoundingBoxMin { get; set; }

    public Vector3 BoundingBoxMax { get; set; }

    public List<ResourceMarker> Markers { get; set; } = [];

    public string? NaviPatch { get; set; }

    public int[]? HeightMapData { get; set; }

    public string SourceFile { get; set; } = string.Empty;
}

public sealed class ResourceMarker
{
    public string ProtoNameHash { get; set; } = string.Empty;

    public string EntityGuid { get; set; } = string.Empty;

    public string LastKnownEntityName { get; set; } = string.Empty;

    public string Modifier1Guid { get; set; } = string.Empty;

    public string Modifier2Guid { get; set; } = string.Empty;

    public string Modifier3Guid { get; set; } = string.Empty;

    public string Modifier1Text { get; set; } = string.Empty;

    public string Modifier2Text { get; set; } = string.Empty;

    public string Modifier3Text { get; set; } = string.Empty;

    public string EncounterSpawnPhase { get; set; } = string.Empty;

    public string FilterGuid { get; set; } = string.Empty;

    public string LastKnownFilterName { get; set; } = string.Empty;

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }
}

public sealed class ClientMapDependency
{
    public string ClientMapName { get; set; } = string.Empty;

    public HashSet<string> RequiredUpks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RequiredPrototypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> SourceCells { get; } = [];

    public string RequiredUpksText => RequiredUpks.Count == 0
        ? "(none)"
        : string.Join(", ", RequiredUpks.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase));

    public string RequiredPrototypesText => RequiredPrototypes.Count == 0
        ? "(none)"
        : string.Join(", ", RequiredPrototypes.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase));

    public string SourceCellsText => SourceCells.Count == 0
        ? "(none)"
        : string.Join(", ", SourceCells);
}

public sealed class ResourcePrototypeScanReport
{
    public string SourcePath { get; set; } = string.Empty;

    public List<string> SourceFiles { get; } = [];

    public List<ClientMapDependency> ClientMapDependencies { get; } = [];

    public List<string> Notes { get; } = [];

    public int ParsedCellCount { get; set; }

    public int ClientMapCount => ClientMapDependencies.Count;

    public string Summary { get; set; } = string.Empty;
}
