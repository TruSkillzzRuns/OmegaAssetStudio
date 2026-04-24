using System;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public static class UpkMigrationSchemaManager
{
    public static UpkMigrationSchemaVersion CurrentVersion { get; } = new("2.0", "UPK Migration Schema");

    public static MigrationEnvelope<T> Wrap<T>(string inspector, T entity)
    {
        if (string.IsNullOrWhiteSpace(inspector))
            throw new ArgumentException("Inspector name is required.", nameof(inspector));

        return new MigrationEnvelope<T>
        {
            SchemaVersion = CurrentVersion.Value,
            Version = CurrentVersion,
            Inspector = inspector,
            CreatedUtc = DateTime.UtcNow,
            Entity = entity
        };
    }

    public static MigrationResultSnapshot CreateSnapshot(MigrationResult result)
    {
        return new MigrationResultSnapshot
        {
            InputUpkPath = result.InputUpkPath,
            OutputUpkPath = result.OutputUpkPath,
            SchemaVersion = result.SchemaVersion,
            AnalyzerVersion = result.AnalyzerVersion,
            CacheKey = result.CacheKey,
            SourceFingerprint = result.SourceFingerprint,
            MigratedMeshes = result.MigratedMeshes,
            MigratedTextures = result.MigratedTextures,
            MigratedAnimations = result.MigratedAnimations,
            MigratedMaterials = result.MigratedMaterials,
            ReferenceCount = result.ReferenceMatches.Count,
            ValidationIssueCount = result.ValidationIssues.Count,
            GraphNodeCount = result.GraphNodes.Count,
            GraphEdgeCount = result.GraphEdges.Count,
            WarningCount = result.Warnings.Count,
            ErrorCount = result.Errors.Count
        };
    }
}

