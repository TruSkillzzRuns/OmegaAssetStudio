using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class SectionCalibrationReport
{
    public string MeshKey { get; set; } = string.Empty;

    public string MeshName { get; set; } = string.Empty;

    public DateTimeOffset CalibratedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SectionCalibrationEntry> Entries { get; set; } = [];

    public string SummaryText => Entries.Count == 0
        ? $"{MeshName}: no sections calibrated."
        : $"{MeshName}: calibrated {Entries.Count} section(s).";

    public bool TryGetEntry(int sectionIndex, out SectionCalibrationEntry? entry)
    {
        entry = Entries.FirstOrDefault(item => item.SectionIndex == sectionIndex);
        return entry is not null;
    }

    public int ResolveMatchingSectionIndex(int sourceSectionIndex, SectionCalibrationReport? targetReport)
    {
        if (targetReport is null || targetReport.Entries.Count == 0)
            return sourceSectionIndex;

        if (!TryGetEntry(sourceSectionIndex, out SectionCalibrationEntry? sourceEntry) || sourceEntry is null)
            return sourceSectionIndex;

        SectionCalibrationEntry? bestMatch = targetReport.Entries
            .Select(entry => new
            {
                Entry = entry,
                GroupScore = string.Equals(entry.BoneGroup, sourceEntry.BoneGroup, StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                TriangleScore = Math.Abs(entry.TriangleCount - sourceEntry.TriangleCount),
                BoneScore = string.Equals(entry.RepresentativeBoneName, sourceEntry.RepresentativeBoneName, StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                DistanceScore = Vector3.Distance(entry.Centroid, sourceEntry.Centroid)
            })
            .OrderBy(item => item.GroupScore)
            .ThenBy(item => item.BoneScore)
            .ThenBy(item => item.TriangleScore)
            .ThenBy(item => item.DistanceScore)
            .Select(item => item.Entry)
            .FirstOrDefault();

        return bestMatch?.SectionIndex ?? sourceSectionIndex;
    }
}

public sealed class SectionCalibrationEntry
{
    public int SectionIndex { get; set; }

    public int RepresentativeBoneIndex { get; set; } = -1;

    public string RepresentativeBoneName { get; set; } = string.Empty;

    public string BoneGroup { get; set; } = "Other";

    public int TriangleCount { get; set; }

    public int VertexCount { get; set; }

    public Vector3 Centroid { get; set; } = Vector3.Zero;

    public BoundingBox Bounds { get; set; } = BoundingBox.Empty;
}

