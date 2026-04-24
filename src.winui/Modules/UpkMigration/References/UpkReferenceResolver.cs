using System;
using System.Collections.Generic;
using System.Linq;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkReferenceResolver
{
    public IReadOnlyList<MigrationReferenceMatch> Resolve(
        Upk148Document document,
        IReadOnlyList<MeshConversionResult> meshes,
        IReadOnlyList<NeutralTexture> textures,
        IReadOnlyList<NeutralAnimation> animations,
        IReadOnlyList<NeutralMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<MigrationReferenceMatch> matches = [];
        List<ReferenceSource> sources = BuildSources(document, meshes, textures, animations, materials);
        List<ReferenceTarget> targets = BuildTargets(document, meshes, textures, animations, materials);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (ReferenceSource source in sources)
        {
            foreach (string candidate in source.Candidates)
            {
                if (!IsVerifiedCandidate(candidate))
                    continue;

                foreach (ReferenceTarget target in targets)
                {
                    MigrationReferenceMatch? match = Evaluate(source, candidate.Trim(), target);
                    if (match is null)
                        continue;

                    string key = $"{match.SourcePath}|{match.TargetPath}|{match.SourceValue}|{match.ResolutionPass}";
                    if (seen.Add(key))
                        matches.Add(match);
                }
            }
        }

        return matches.OrderByDescending(item => item.Confidence).ThenBy(item => item.SourcePath).ThenBy(item => item.TargetPath).ToArray();
    }

    private static List<ReferenceSource> BuildSources(
        Upk148Document document,
        IReadOnlyList<MeshConversionResult> meshes,
        IReadOnlyList<NeutralTexture> textures,
        IReadOnlyList<NeutralAnimation> animations,
        IReadOnlyList<NeutralMaterial> materials)
    {
        List<ReferenceSource> sources = [];

        foreach (var export in document.Header.ExportTable.Entries)
        {
            sources.Add(new ReferenceSource(export.PathName, export.ClassName, export.ObjectName, export.ResolvedType, export.PathName));
        }

        foreach (var mesh in meshes)
            sources.Add(new ReferenceSource(mesh.Mesh.Name, mesh.Mesh.IsSkeletal ? "SkeletalMesh" : "StaticMesh", mesh.Mesh.Name, string.Join(" ", mesh.Mesh.MaterialSlots.Select(slot => slot.MaterialPath).Where(item => !string.IsNullOrWhiteSpace(item))), mesh.Skeleton.Name));

        foreach (var texture in textures)
            sources.Add(new ReferenceSource(texture.SourcePath ?? texture.Name, "Texture", texture.Name, string.Join(" ", texture.Notes), texture.Format));

        foreach (var animation in animations)
            sources.Add(new ReferenceSource(animation.Name, "Animation", animation.Name, string.Join(" ", animation.Tracks.Select(track => track.BoneName)), animation.Name));

        foreach (var material in materials)
            sources.Add(new ReferenceSource(material.Name, "Material", material.Name, string.Join(" ", material.ReferencedTextures), material.Metadata));

        return sources;
    }

    private static List<ReferenceTarget> BuildTargets(
        Upk148Document document,
        IReadOnlyList<MeshConversionResult> meshes,
        IReadOnlyList<NeutralTexture> textures,
        IReadOnlyList<NeutralAnimation> animations,
        IReadOnlyList<NeutralMaterial> materials)
    {
        List<ReferenceTarget> targets = [];

        foreach (var export in document.Header.ExportTable.Entries)
        {
            targets.Add(new ReferenceTarget(export.PathName, export.ObjectName, export.ClassName, export.PathName, export.ClassName));
        }

        foreach (var mesh in meshes)
            targets.Add(new ReferenceTarget(mesh.Mesh.Name, mesh.Mesh.Name, "Mesh", mesh.Mesh.Name, mesh.Mesh.Skeleton?.Name ?? string.Empty));

        foreach (var texture in textures)
            targets.Add(new ReferenceTarget(texture.Name, texture.Name, "Texture", texture.Name, texture.Format));

        foreach (var animation in animations)
            targets.Add(new ReferenceTarget(animation.Name, animation.Name, "Animation", animation.Name, animation.Name));

        foreach (var material in materials)
            targets.Add(new ReferenceTarget(material.Name, material.Name, "Material", material.Name, material.Metadata));

        return targets;
    }

    private static MigrationReferenceMatch? Evaluate(ReferenceSource source, string candidate, ReferenceTarget target)
    {
        if (LooksVerifiedDirect(candidate, target))
        {
            return CreateMatch(source, candidate, target, "Direct", UpkReferenceConfidence.High);
        }

        return null;
    }

    private static MigrationReferenceMatch CreateMatch(ReferenceSource source, string candidate, ReferenceTarget target, string pass, UpkReferenceConfidence confidence)
    {
        return new MigrationReferenceMatch
        {
            SourcePath = source.SourcePath,
            SourceValue = candidate,
            SourceKind = source.SourceKind,
            TargetPath = target.Path,
            TargetValue = target.Label,
            TargetKind = target.Kind,
            ResolutionPass = pass,
            Confidence = confidence switch
            {
                UpkReferenceConfidence.High => MigrationReferenceConfidence.High,
                UpkReferenceConfidence.Medium => MigrationReferenceConfidence.Medium,
                _ => MigrationReferenceConfidence.Low
            },
            Evidence = source.Evidence,
            Details = target.Details
        };
    }

    private static bool IsVerifiedCandidate(string candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate);
    }

    private static bool LooksVerifiedDirect(string candidate, ReferenceTarget target)
    {
        if (string.Equals(candidate, target.Path, StringComparison.OrdinalIgnoreCase))
            return true;

        string fileName = System.IO.Path.GetFileName(target.Path);
        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(target.Path);
        return string.Equals(candidate, fileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, nameNoExt, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ReferenceSource(string SourcePath, string SourceKind, string Label, string Evidence, string? Metadata)
    {
        public IEnumerable<string> Candidates
        {
            get
            {
                yield return Label;
                yield return SourcePath;
                if (!string.IsNullOrWhiteSpace(Evidence))
                    yield return Evidence;
                if (!string.IsNullOrWhiteSpace(Metadata))
                    yield return Metadata!;
            }
        }
    }

    private sealed record ReferenceTarget(string Path, string Label, string Kind, string SearchText, string? Details);
}

