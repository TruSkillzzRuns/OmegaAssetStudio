using OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk152Builder;

public sealed class MigrationPackagePlan
{
    public Upk148Document SourceDocument { get; set; } = null!;
    public List<MeshConversionResult> Meshes { get; } = [];
    public List<NeutralTexture> Textures { get; } = [];
    public List<NeutralAnimation> Animations { get; } = [];
    public List<NeutralMaterial> Materials { get; } = [];
    public List<MigrationReferenceMatch> References { get; } = [];
    public List<MigrationValidationIssue> ValidationIssues { get; } = [];
    public List<MigrationGraphNode> GraphNodes { get; } = [];
    public List<MigrationGraphEdge> GraphEdges { get; } = [];
    public List<string> Warnings { get; } = [];
    public Upk152Header Header { get; set; } = new();
    public Upk152NameTable NameTable { get; set; } = new();
    public Upk152ExportTable ExportTable { get; set; } = new();
}

public sealed class Upk152AssetInjector
{
    public MigrationPackagePlan BuildPlan(
        Upk148Document sourceDocument,
        IReadOnlyList<MeshConversionResult> meshes,
        IReadOnlyList<NeutralTexture> textures,
        IReadOnlyList<NeutralAnimation> animations,
        IReadOnlyList<NeutralMaterial> materials,
        MigrationResult migrationResult,
        IEnumerable<string>? warnings = null)
    {
        MigrationPackagePlan plan = new()
        {
            SourceDocument = sourceDocument,
            Header = new Upk152Header
            {
                SourcePath = sourceDocument.SourcePath,
                SourceVersion = sourceDocument.Header.Version,
                TargetVersion = 152,
                Licensee = sourceDocument.Header.Licensee,
                Flags = sourceDocument.Header.Flags,
                EngineVersion = sourceDocument.RawHeader.EngineVersion,
                CookerVersion = sourceDocument.RawHeader.CookerVersion,
                ImportCount = sourceDocument.RawHeader.ImportTable.Count,
                ExportCount = sourceDocument.RawHeader.ExportTable.Count,
                NameCount = sourceDocument.RawHeader.NameTable.Count
            }
        };

        plan.Meshes.AddRange(meshes);
        plan.Textures.AddRange(textures);
        plan.Animations.AddRange(animations);
        plan.Materials.AddRange(materials);
        plan.Warnings.AddRange(warnings ?? []);
        plan.Header.OutputPath = migrationResult.OutputUpkPath;

        plan.NameTable.Names.AddRange(sourceDocument.Header.NameStrings);
        foreach (MeshConversionResult mesh in meshes)
            plan.ExportTable.Entries.Add(new Upk152ExportEntry { PathName = mesh.Mesh.Name, ClassName = "Mesh", ObjectName = mesh.Mesh.Name, AssetType = mesh.Mesh.IsSkeletal ? "SkeletalMesh" : "StaticMesh", SerialSize = mesh.Mesh.Vertices.Count });
        foreach (NeutralTexture texture in textures)
            plan.ExportTable.Entries.Add(new Upk152ExportEntry { PathName = texture.Name, ClassName = "Texture2D", ObjectName = texture.Name, AssetType = "Texture", SerialSize = texture.PixelData.Length });
        foreach (NeutralAnimation animation in animations)
            plan.ExportTable.Entries.Add(new Upk152ExportEntry { PathName = animation.Name, ClassName = "AnimSequence", ObjectName = animation.Name, AssetType = "Animation", SerialSize = animation.FrameCount });
        foreach (NeutralMaterial material in materials)
            plan.ExportTable.Entries.Add(new Upk152ExportEntry { PathName = material.Name, ClassName = "Material", ObjectName = material.Name, AssetType = "Material", SerialSize = material.ReferencedTextures.Count });

        migrationResult.MigratedMeshes = meshes.Count;
        migrationResult.MigratedTextures = textures.Count;
        migrationResult.MigratedAnimations = animations.Count;
        migrationResult.MigratedMaterials = materials.Count;
        migrationResult.SchemaVersion = UpkMigrationSchemaManager.CurrentVersion.Value;
        migrationResult.AnalyzerVersion = UpkMigrationVersions.AnalyzerVersion;
        migrationResult.Envelope = UpkMigrationSchemaManager.Wrap("Upk152AssetInjector", UpkMigrationSchemaManager.CreateSnapshot(migrationResult));
        return plan;
    }
}

