using System.IO;

namespace OmegaAssetStudio.WinUI.Models;

public sealed class RecentUpkEntry
{
    public string UpkPath { get; set; } = string.Empty;
    public string WorkspaceTag { get; set; } = string.Empty;
    public string ExportPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string LastUsedText { get; set; } = string.Empty;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Path.GetFileName(UpkPath) : Title;
}

