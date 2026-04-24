using System.Collections.Generic;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;

public sealed class UpkSummary
{
    public int NameCount { get; set; }

    public int NameOffset { get; set; }

    public int ExportCount { get; set; }

    public int ExportOffset { get; set; }

    public int ImportCount { get; set; }

    public int ImportOffset { get; set; }

    public int FileSize { get; set; }

    public List<UpkGenerationInfo> Generations { get; } = [];
}
