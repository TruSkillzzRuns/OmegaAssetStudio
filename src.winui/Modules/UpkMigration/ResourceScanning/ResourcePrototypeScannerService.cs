using System.Text.Json;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Configuration;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.ResourceScanning;

public sealed class ResourcePrototypeScannerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ResourcePrototypeScanReport> ScanAsync(string sourcePath, UpkMigrationConfig? config = null, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("A resource prototype source path is required.");

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath) && !Directory.Exists(fullSourcePath))
            throw new FileNotFoundException("The selected resource prototype source path could not be found.", fullSourcePath);

        config ??= UpkMigrationConfigStore.Load();

        List<string> files = EnumerateCandidateFiles(fullSourcePath).ToList();
        ResourcePrototypeScanReport report = new()
        {
            SourcePath = fullSourcePath
        };
        report.SourceFiles.AddRange(files);

        Dictionary<string, ClientMapDependency> dependencies = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            log?.Invoke($"Scanning resource prototype file: {Path.GetFileName(file)}");
            foreach (ResourcePrototypeCell cell in await ReadCellsAsync(file).ConfigureAwait(false))
            {
                report.ParsedCellCount++;
                if (string.IsNullOrWhiteSpace(cell.ClientMap))
                    continue;

                ClientMapDependency dependency = GetOrCreateDependency(dependencies, cell.ClientMap);
                dependency.SourceCells.Add(string.IsNullOrWhiteSpace(cell.SourceFile) ? Path.GetFileName(file) : cell.SourceFile);
                AddRequiredUpk(dependency, cell.ClientMap, config);

                foreach (ResourceMarker marker in cell.Markers ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(marker.LastKnownEntityName))
                    {
                        dependency.RequiredPrototypes.Add(marker.LastKnownEntityName.Trim());
                        ResolvePrototypeMappings(dependency, marker.LastKnownEntityName.Trim(), config);
                    }
                }
            }
        }

        foreach (ClientMapDependency dependency in dependencies.Values.OrderBy(static item => item.ClientMapName, StringComparer.OrdinalIgnoreCase))
            report.ClientMapDependencies.Add(dependency);

        report.Notes.Add($"Loaded {report.SourceFiles.Count:N0} file(s).");
        report.Notes.Add($"Parsed {report.ParsedCellCount:N0} resource prototype cell(s).");
        report.Notes.Add($"Discovered {report.ClientMapDependencies.Count:N0} ClientMap dependency set(s).");
        report.Summary = string.Join(" ", report.Notes);

        return report;
    }

    public async Task<string> ExportAsync(ResourcePrototypeScanReport report, string outputDirectory, string? logPath = null)
    {
        if (report is null)
            throw new ArgumentNullException(nameof(report));

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("An output directory is required for the resource prototype scan report.");

        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        JsonSerializerOptions jsonOptions = new(JsonOptions)
        {
            WriteIndented = true
        };

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string jsonPath = Path.Combine(fullOutputDirectory, $"ResourcePrototypeScanReport_{stamp}.json");
        string dependenciesPath = Path.Combine(fullOutputDirectory, "ClientMapDependencies.json");
        string markdownPath = Path.Combine(fullOutputDirectory, $"ResourcePrototypeScanReport_{stamp}.md");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(dependenciesPath, JsonSerializer.Serialize(report.ClientMapDependencies, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(report)).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)) ?? fullOutputDirectory);
            await File.AppendAllTextAsync(logPath, $"[{DateTime.Now:HH:mm:ss}] Scan exported to {fullOutputDirectory}{Environment.NewLine}").ConfigureAwait(false);
        }

        return fullOutputDirectory;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            if (sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                yield return sourcePath;
                yield break;
            }

            throw new NotSupportedException("Direct SIP parsing is not wired in this build. Provide extracted resource prototype JSON files or a directory containing them.");
        }

        foreach (string file in Directory.EnumerateFiles(sourcePath, "*.json", SearchOption.AllDirectories))
            yield return file;
    }

    private static async Task<IReadOnlyList<ResourcePrototypeCell>> ReadCellsAsync(string filePath)
    {
        string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<ResourcePrototypeCell> cells = [];
        if (root.ValueKind == JsonValueKind.Array)
        {
            cells.AddRange(JsonSerializer.Deserialize<List<ResourcePrototypeCell>>(json, JsonOptions) ?? []);
        }
        else if (TryGetArray(root, "Cells", out JsonElement cellsArray) || TryGetArray(root, "ResourcePrototypes", out cellsArray) || TryGetArray(root, "Items", out cellsArray))
        {
            cells.AddRange(JsonSerializer.Deserialize<List<ResourcePrototypeCell>>(cellsArray.GetRawText(), JsonOptions) ?? []);
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            ResourcePrototypeCell? cell = JsonSerializer.Deserialize<ResourcePrototypeCell>(json, JsonOptions);
            if (cell is not null)
            {
                cell.SourceFile = Path.GetFileName(filePath);
                cells.Add(cell);
            }
        }

        foreach (ResourcePrototypeCell cell in cells)
            cell.SourceFile = string.IsNullOrWhiteSpace(cell.SourceFile) ? Path.GetFileName(filePath) : cell.SourceFile;

        return cells;
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out array) && array.ValueKind == JsonValueKind.Array)
            return true;

        array = default;
        return false;
    }

    private static ClientMapDependency GetOrCreateDependency(Dictionary<string, ClientMapDependency> dependencies, string clientMapName)
    {
        if (!dependencies.TryGetValue(clientMapName, out ClientMapDependency? dependency))
        {
            dependency = new ClientMapDependency
            {
                ClientMapName = clientMapName
            };
            dependencies[clientMapName] = dependency;
        }

        return dependency;
    }

    private void AddRequiredUpk(ClientMapDependency dependency, string clientMapName, UpkMigrationConfig config)
    {
        foreach (string candidate in ResolveUpkCandidates(clientMapName, config))
            dependency.RequiredUpks.Add(candidate);
    }

    private IEnumerable<string> ResolveUpkCandidates(string clientMapName, UpkMigrationConfig config)
    {
        if (config.ClientMapUpkMap.TryGetValue(clientMapName, out string? mapped) && !string.IsNullOrWhiteSpace(mapped))
            yield return NormalizeUpkName(mapped);

        foreach (string root in new[] { config.GameRoot152, config.GameRoot148 })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string candidatePath = Path.Combine(Path.GetFullPath(root), $"{clientMapName}.upk");
            if (File.Exists(candidatePath))
                yield return Path.GetFileNameWithoutExtension(candidatePath);
        }

        yield return clientMapName;
    }

    private void ResolvePrototypeMappings(ClientMapDependency dependency, string prototypePath, UpkMigrationConfig config)
    {
        foreach (KeyValuePair<string, string> rule in config.PrototypeUpkMap)
        {
            if (string.IsNullOrWhiteSpace(rule.Key) || string.IsNullOrWhiteSpace(rule.Value))
                continue;

            if (rule.Key.EndsWith("*", StringComparison.Ordinal))
            {
                string prefix = rule.Key[..^1];
                if (prototypePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    dependency.RequiredUpks.Add(NormalizeUpkName(rule.Value));
            }
            else if (prototypePath.Equals(rule.Key, StringComparison.OrdinalIgnoreCase))
            {
                dependency.RequiredUpks.Add(NormalizeUpkName(rule.Value));
            }
        }
    }

    private static string NormalizeUpkName(string value)
    {
        string trimmed = value.Trim();
        return Path.GetFileNameWithoutExtension(trimmed);
    }

    private static string BuildMarkdown(ResourcePrototypeScanReport report)
    {
        System.Text.StringBuilder builder = new();
        builder.AppendLine("# Resource Prototype Scan Report");
        builder.AppendLine();
        builder.AppendLine(report.Summary);
        builder.AppendLine();

        foreach (ClientMapDependency dependency in report.ClientMapDependencies)
        {
            builder.AppendLine($"## {dependency.ClientMapName}");
            builder.AppendLine($"- Required UPKs: {(dependency.RequiredUpks.Count == 0 ? "(none)" : string.Join(", ", dependency.RequiredUpks.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)))}");
            builder.AppendLine($"- Required Prototypes: {(dependency.RequiredPrototypes.Count == 0 ? "(none)" : string.Join(", ", dependency.RequiredPrototypes.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)))}");
            builder.AppendLine($"- Source Cells: {(dependency.SourceCells.Count == 0 ? "(none)" : string.Join(", ", dependency.SourceCells))}");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
