using System.Collections.Generic;
using UpkManager.Models.UpkFile;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;

public sealed class UpkPackage
{
    public string OriginalPath { get; set; } = string.Empty;

    public UnrealHeader? SourceHeader { get; set; }

    public UpkSummary Summary { get; set; } = new();

    public List<UpkNameEntry> Names { get; } = [];

    public List<UpkImportEntry> Imports { get; } = [];

    public List<UpkExportEntry> Exports { get; } = [];

    public UpkObjectSelection Selection { get; } = new();
}
