using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

internal sealed class OmegaIntelKnowledgeGraphService
{
    public void BuildGraph(OmegaIntelScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var nodes = new Dictionary<string, OmegaIntelGraphNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<OmegaIntelGraphEdge>();

        foreach (var file in result.Files)
        {
            string fileId = GetFileNodeId(file.Path);
            nodes[fileId] = new OmegaIntelGraphNode
            {
                Id = fileId,
                Kind = "File",
                Label = file.Name,
                SourcePath = file.Path,
                Description = file.Classification,
                Weight = 1
            };

            foreach (string tag in file.Tags ?? Array.Empty<string>())
            {
                string tagId = $"tag:{Normalize(tag)}";
                if (!nodes.ContainsKey(tagId))
                {
                    nodes[tagId] = new OmegaIntelGraphNode
                    {
                        Id = tagId,
                        Kind = "Tag",
                        Label = tag,
                        Weight = 0.5
                    };
                }

                edges.Add(new OmegaIntelGraphEdge
                {
                    FromId = fileId,
                    ToId = tagId,
                    Label = "has-tag"
                });
            }

            foreach (var insight in file.Insights ?? Array.Empty<OmegaIntelInsight>())
            {
                string insightId = $"insight:{Normalize(insight.Kind)}:{Normalize(insight.Value)}";
                if (!nodes.ContainsKey(insightId))
                {
                    nodes[insightId] = new OmegaIntelGraphNode
                    {
                        Id = insightId,
                        Kind = insight.Kind,
                        Label = insight.Value,
                        Description = insight.Details,
                        Weight = 0.75
                    };
                }

                edges.Add(new OmegaIntelGraphEdge
                {
                    FromId = fileId,
                    ToId = insightId,
                    Label = "contains"
                });
            }
        }

        foreach (var directory in result.Directories)
        {
            string directoryId = $"directory:{Normalize(directory.Path)}";
            nodes[directoryId] = new OmegaIntelGraphNode
            {
                Id = directoryId,
                Kind = "Directory",
                Label = string.IsNullOrWhiteSpace(directory.Path) ? "(root)" : Path.GetFileName(directory.Path),
                SourcePath = directory.Path,
                Description = $"Files={directory.FileCount}, Bytes={directory.TotalBytes:N0}",
                Weight = Math.Max(0.5, directory.FileCount)
            };
        }

        foreach (var map in result.TfcMap)
        {
            string tfcId = $"tfc:{Normalize(map.TfcPath ?? map.CacheName)}";
            if (!nodes.ContainsKey(tfcId))
            {
                nodes[tfcId] = new OmegaIntelGraphNode
                {
                    Id = tfcId,
                    Kind = "TFC",
                    Label = string.IsNullOrWhiteSpace(map.CacheName) ? "(unknown cache)" : map.CacheName,
                    SourcePath = map.TfcPath,
                    Description = map.TextureFormat,
                    Weight = 0.9
                };
            }

            string textureId = $"texture:{Normalize(map.TextureName)}";
            if (!nodes.ContainsKey(textureId))
            {
                nodes[textureId] = new OmegaIntelGraphNode
                {
                    Id = textureId,
                    Kind = "TextureRef",
                    Label = map.TextureName,
                    Description = map.ManifestPath,
                    Weight = 0.7
                };
            }

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = textureId,
                ToId = tfcId,
                Label = map.CacheFileExists ? "resolved-by" : "cache-candidate"
            });
        }

        foreach (var uiHero in result.UiHeroEntriesList)
        {
            string uiId = $"uihero:{Normalize(uiHero.Path)}";
            nodes[uiId] = new OmegaIntelGraphNode
            {
                Id = uiId,
                Kind = "UIHero",
                Label = uiHero.DisplayName,
                SourcePath = uiHero.Path,
                Description = uiHero.HeroId,
                Weight = 0.8
            };

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = GetFileNodeId(uiHero.Path),
                ToId = uiId,
                Label = "ui-hero-candidate"
            });
        }

        foreach (var candidate in result.HeroIdCandidatesList)
        {
            string heroId = $"heroid:{Normalize(candidate.Candidate)}";
            if (!nodes.ContainsKey(heroId))
            {
                nodes[heroId] = new OmegaIntelGraphNode
                {
                    Id = heroId,
                    Kind = "HeroId",
                    Label = candidate.Candidate,
                    SourcePath = candidate.Path,
                    Description = candidate.Evidence,
                    Weight = 0.6
                };
            }

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = GetFileNodeId(candidate.Path),
                ToId = heroId,
                Label = "exe-candidate"
            });
        }

        foreach (var candidate in result.RosterTableCandidatesList)
        {
            string rosterId = $"roster:{Normalize(candidate.Candidate)}";
            if (!nodes.ContainsKey(rosterId))
            {
                nodes[rosterId] = new OmegaIntelGraphNode
                {
                    Id = rosterId,
                    Kind = "Roster",
                    Label = candidate.Candidate,
                    SourcePath = candidate.Path,
                    Description = candidate.Evidence,
                    Weight = 0.6
                };
            }

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = GetFileNodeId(candidate.Path),
                ToId = rosterId,
                Label = "roster-candidate"
            });
        }

        foreach (var candidate in result.PowerTreeCandidatesList)
        {
            string powerId = $"power:{Normalize(candidate.Candidate)}";
            if (!nodes.ContainsKey(powerId))
            {
                nodes[powerId] = new OmegaIntelGraphNode
                {
                    Id = powerId,
                    Kind = "PowerTree",
                    Label = candidate.Candidate,
                    SourcePath = candidate.Path,
                    Description = candidate.Evidence,
                    Weight = 0.6
                };
            }

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = GetFileNodeId(candidate.Path),
                ToId = powerId,
                Label = "power-candidate"
            });
        }

        foreach (var entry in result.StringTableEntriesList.Take(200))
        {
            string stringId = $"string:{Normalize(entry.Value)}:{entry.Offset?.ToString() ?? "0"}";
            if (!nodes.ContainsKey(stringId))
            {
                nodes[stringId] = new OmegaIntelGraphNode
                {
                    Id = stringId,
                    Kind = "String",
                    Label = entry.Value,
                    SourcePath = entry.Path,
                    Description = entry.Category,
                    Weight = 0.35
                };
            }

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = GetFileNodeId(entry.Path),
                ToId = stringId,
                Label = "string-table"
            });
        }

        foreach (var entry in result.FunctionSignatureEntriesList)
        {
            string sigId = $"signature:{Normalize(entry.Signature)}";
            if (!nodes.ContainsKey(sigId))
            {
                nodes[sigId] = new OmegaIntelGraphNode
                {
                    Id = sigId,
                    Kind = "Signature",
                    Label = entry.Signature,
                    SourcePath = entry.Path,
                    Description = entry.Evidence,
                    Weight = 0.5
                };
            }

            edges.Add(new OmegaIntelGraphEdge
            {
                FromId = GetFileNodeId(entry.Path),
                ToId = sigId,
                Label = "function-signature"
            });
        }

        result.Nodes = nodes.Values.OrderBy(node => node.Kind).ThenBy(node => node.Label).ToList();
        result.Edges = edges;
    }

    private static string GetFileNodeId(string path) => $"file:{Normalize(path)}";

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        Span<char> buffer = stackalloc char[value.Length];
        int index = 0;
        foreach (char c in value.ToLowerInvariant())
        {
            buffer[index++] = char.IsLetterOrDigit(c) ? c : '_';
        }

        return new string(buffer[..index]);
    }
}

