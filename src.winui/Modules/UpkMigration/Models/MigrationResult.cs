using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class MigrationResult
{
    public string InputUpkPath { get; set; } = string.Empty;
    public string OutputUpkPath { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string AnalyzerVersion { get; set; } = string.Empty;
    public string CacheKey { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public int MigratedMeshes { get; set; }
    public int MigratedTextures { get; set; }
    public int MigratedAnimations { get; set; }
    public int MigratedMaterials { get; set; }
    public List<MigrationReferenceMatch> ReferenceMatches { get; } = [];
    public List<MigrationValidationIssue> ValidationIssues { get; } = [];
    public List<MigrationGraphNode> GraphNodes { get; } = [];
    public List<MigrationGraphEdge> GraphEdges { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public MigrationEnvelope<MigrationResultSnapshot>? Envelope { get; set; }

    public bool Success => Errors.Count == 0;
    [JsonIgnore]
    public bool HasValidationErrors => ValidationIssues.Any(item => item.Severity.Equals("Error", System.StringComparison.OrdinalIgnoreCase));

    public string SummaryText =>
        $"Input: {InputUpkPath}{System.Environment.NewLine}" +
        $"Output: {OutputUpkPath}{System.Environment.NewLine}" +
        $"Meshes: {MigratedMeshes}  Textures: {MigratedTextures}  Animations: {MigratedAnimations}  Materials: {MigratedMaterials}{System.Environment.NewLine}" +
        $"Warnings: {Warnings.Count}  Errors: {Errors.Count}  Validation: {ValidationIssues.Count}";
}

public sealed class MigrationValidationIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Details { get; set; }
}

public sealed class MigrationReferenceMatch
{
    public string SourcePath { get; set; } = string.Empty;
    public string SourceValue { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string? TargetValue { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? TargetKind { get; set; }
    public string ResolutionPass { get; set; } = string.Empty;
    public MigrationReferenceConfidence Confidence { get; set; }
    public string? Evidence { get; set; }
    public string? Details { get; set; }
}

public enum MigrationReferenceConfidence
{
    Low,
    Medium,
    High
}

public sealed class MigrationGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public string? Details { get; set; }
}

public sealed class MigrationGraphEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class MigrationResultSnapshot
{
    public string InputUpkPath { get; set; } = string.Empty;
    public string OutputUpkPath { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string AnalyzerVersion { get; set; } = string.Empty;
    public string CacheKey { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public int MigratedMeshes { get; set; }
    public int MigratedTextures { get; set; }
    public int MigratedAnimations { get; set; }
    public int MigratedMaterials { get; set; }
    public int ReferenceCount { get; set; }
    public int ValidationIssueCount { get; set; }
    public int GraphNodeCount { get; set; }
    public int GraphEdgeCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

