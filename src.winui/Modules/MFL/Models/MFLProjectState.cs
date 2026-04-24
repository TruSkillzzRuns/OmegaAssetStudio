namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class MFLProjectState
{
    public string ProjectName { get; set; } = "DefaultProject";

    public string MeshAPath { get; set; } = string.Empty;

    public string MeshAUpkPath { get; set; } = string.Empty;

    public string MeshAExportPath { get; set; } = string.Empty;

    public string MeshBPath { get; set; } = string.Empty;

    public string MeshBUpkPath { get; set; } = string.Empty;

    public string MeshBExportPath { get; set; } = string.Empty;

    public List<string> AdditionalMeshPaths { get; set; } = [];

    public string ExtractedRegionPath { get; set; } = string.Empty;

    public string FusedMeshPath { get; set; } = string.Empty;

    public string ExportPath { get; set; } = "UnifiedMesh";

    public string SelectedSelectionMode { get; set; } = "Raycast";

    public string SelectedBoneName { get; set; } = "Root";

    public string SelectedSectionText { get; set; } = "0";

    public string RayOriginText { get; set; } = "0, 1, 5";

    public string RayDirectionText { get; set; } = "0, 0, -1";

    public string SelectedExportFormat { get; set; } = "PSK";

    public string SelectedPreviewFocus { get; set; } = "Auto";

    public string SelectedMeshKey { get; set; } = "MeshA";

    public bool MeshAVisible { get; set; } = true;

    public bool MeshBVisible { get; set; } = true;

    public bool MeshAWireframe { get; set; } = false;

    public bool MeshBWireframe { get; set; } = false;

    public bool GhostInactiveMesh { get; set; } = true;

    public bool ShowGroundPlane { get; set; } = false;

    public RegionSelectionState? CurrentSelection { get; set; }
}

