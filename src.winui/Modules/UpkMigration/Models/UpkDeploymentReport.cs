using System.Text.Json.Serialization;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class UpkDeploymentReport
{
    public string SourceUpkPath { get; set; } = string.Empty;

    public string TargetClientRoot { get; set; } = string.Empty;

    public string DeployFileName { get; set; } = string.Empty;

    public string DeployedPath { get; set; } = string.Empty;

    public string ClientMapName { get; set; } = string.Empty;

    public bool RefreshPackageIndex { get; set; }

    public string? PackageIndexPath { get; set; }

    public List<string> Notes { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    [JsonIgnore]
    public bool Success => Errors.Count == 0;

    [JsonIgnore]
    public string SummaryText =>
        $"Source: {Path.GetFileName(SourceUpkPath)}  Deployed: {Path.GetFileName(DeployedPath)}  Index: {(RefreshPackageIndex ? "refreshed" : "skipped")}  Errors: {Errors.Count}";
}
