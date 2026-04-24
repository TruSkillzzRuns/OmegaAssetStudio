using System;
using System.Collections.Generic;
using System.Linq;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkGraphSanityChecker
{
    public void Run(MigrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        result.GraphNodes.Clear();
        result.GraphEdges.Clear();

        Dictionary<string, MigrationGraphNode> nodes = new(StringComparer.OrdinalIgnoreCase);
        List<MigrationGraphEdge> edges = [];

        foreach (MigrationReferenceMatch match in result.ReferenceMatches)
        {
            string sourceId = Normalize(match.SourcePath);
            string targetId = Normalize(match.TargetPath ?? match.TargetValue ?? string.Empty);

            AddNode(nodes, sourceId, match.SourceValue, match.SourceKind, match.SourcePath, match.Evidence);
            AddNode(nodes, targetId, match.TargetValue ?? match.TargetPath ?? string.Empty, match.TargetKind ?? "Target", match.TargetPath, match.Details);

            edges.Add(new MigrationGraphEdge
            {
                FromId = sourceId,
                ToId = targetId,
                Label = match.ResolutionPass
            });
        }

        foreach (var issue in ValidateDuplicates(nodes.Values, edges))
            result.ValidationIssues.Add(issue);

        result.GraphNodes.AddRange(nodes.Values.OrderBy(node => node.Kind).ThenBy(node => node.Label));
        result.GraphEdges.AddRange(Deduplicate(edges));
    }

    private static IEnumerable<MigrationValidationIssue> ValidateDuplicates(IEnumerable<MigrationGraphNode> nodes, IEnumerable<MigrationGraphEdge> edges)
    {
        HashSet<string> nodeLabels = new(StringComparer.OrdinalIgnoreCase);
        foreach (MigrationGraphNode node in nodes)
        {
            if (!nodeLabels.Add(node.Label))
            {
                yield return new MigrationValidationIssue
                {
                    Severity = "Warning",
                    Code = "DUPLICATE_NODE",
                    Message = $"Duplicate graph node label: {node.Label}",
                    Source = node.SourcePath,
                    Details = node.Kind
                };
            }
        }

        HashSet<string> edgeKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (MigrationGraphEdge edge in edges)
        {
            string key = $"{edge.FromId}|{edge.ToId}|{edge.Label}";
            if (!edgeKeys.Add(key))
            {
                yield return new MigrationValidationIssue
                {
                    Severity = "Warning",
                    Code = "DUPLICATE_EDGE",
                    Message = $"Duplicate graph edge: {edge.FromId} -> {edge.ToId}",
                    Details = edge.Label
                };
            }
        }
    }

    private static List<MigrationGraphEdge> Deduplicate(IEnumerable<MigrationGraphEdge> edges)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<MigrationGraphEdge> deduped = [];
        foreach (MigrationGraphEdge edge in edges)
        {
            string key = $"{edge.FromId}|{edge.ToId}|{edge.Label}";
            if (seen.Add(key))
                deduped.Add(edge);
        }

        return deduped;
    }

    private static void AddNode(Dictionary<string, MigrationGraphNode> nodes, string id, string label, string kind, string? sourcePath, string? details)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (!nodes.ContainsKey(id))
        {
            nodes[id] = new MigrationGraphNode
            {
                Id = id,
                Label = label,
                Kind = kind,
                SourcePath = sourcePath,
                Details = details
            };
        }
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        Span<char> buffer = stackalloc char[value.Length];
        int index = 0;
        foreach (char c in value.ToLowerInvariant())
            buffer[index++] = char.IsLetterOrDigit(c) ? c : '_';

        return new string(buffer[..index]);
    }
}

