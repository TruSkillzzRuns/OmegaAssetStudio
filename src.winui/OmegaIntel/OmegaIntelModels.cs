using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

public enum OmegaIntelFileKind
{
    Unknown,
    Upk,
    Tfc,
    Texture,
    Mesh,
    Animation,
    Character,
    Ui,
    Executable,
    Config,
    Script,
    Library,
    Data,
}

public sealed class OmegaIntelScanOptions
{
    public string RootPath { get; set; } = string.Empty;
    public bool DeepUpkAnalysis { get; set; } = true;
    public bool AnalyzeExecutables { get; set; } = true;
    public bool AnalyzeUpkFiles { get; set; } = true;
    public bool AnalyzeTfcFiles { get; set; } = true;
    public bool AnalyzeTextureFiles { get; set; } = true;
    public bool AnalyzeMeshFiles { get; set; } = true;
    public bool AnalyzeAnimationFiles { get; set; } = true;
    public bool AnalyzeCharacterFiles { get; set; } = true;
    public bool AnalyzeUiFiles { get; set; } = true;
    public bool BuildKnowledgeGraph { get; set; } = true;
}

public sealed class OmegaIntelScanResult
{
    public string RootPath { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public string? Summary { get; set; }
    public string? Notes { get; set; }
    public int TotalFiles { get; set; }
    public int DirectoryCount { get; set; }
    public int ClassifiedFiles { get; set; }
    public int UpkFiles { get; set; }
    public int TfcFiles { get; set; }
    public int TfcManifestFiles { get; set; }
    public int TfcEntriesTotal { get; set; }
    public int TextureFiles { get; set; }
    public int MeshFiles { get; set; }
    public int AnimationFiles { get; set; }
    public int CharacterFiles { get; set; }
    public int UiFiles { get; set; }
    public int CharacterLikeUpks { get; set; }
    public int UiLikeUpks { get; set; }
    public int TextureLikeUpks { get; set; }
    public int MeshLikeUpks { get; set; }
    public int AnimationLikeUpks { get; set; }
    public int ResolvedTextureUpks { get; set; }
    public int ResolvedMeshUpks { get; set; }
    public int ResolvedSkeletalMeshUpks { get; set; }
    public int ResolvedStaticMeshUpks { get; set; }
    public int ResolvedAnimSetUpks { get; set; }
    public int ResolvedCharacterUpks { get; set; }
    public int CacheBackedTextureUpks { get; set; }
    public int AnimSequenceTotal { get; set; }
    public int SkeletalBoneTotal { get; set; }
    public int ExecutableFiles { get; set; }
    public int ConfigFiles { get; set; }
    public int ScriptFiles { get; set; }
    public int LibraryFiles { get; set; }
    public int DataFiles { get; set; }
    public int UnknownFiles { get; set; }
    public int HighEntropyFiles { get; set; }
    public int UnknownMagicFiles { get; set; }
    public int UiHeroEntries { get; set; }
    public int ExePatternHits { get; set; }
    public int HeroIdCandidates { get; set; }
    public int RosterTableCandidates { get; set; }
    public int PowerTreeCandidates { get; set; }
    public int StringTableEntries { get; set; }
    public int FunctionSignatureEntries { get; set; }
    public int ExeSectionEntries { get; set; }
    public int ValidationIssues { get; set; }
    public int ValidationWarnings { get; set; }
    public int ValidationErrors { get; set; }
    public List<OmegaIntelFileRecord> Files { get; set; } = [];
    public List<OmegaIntelDirectoryRecord> Directories { get; set; } = [];
    public List<OmegaIntelTfcMapEntry> TfcMap { get; set; } = [];
    public List<OmegaIntelUiHeroEntry> UiHeroEntriesList { get; set; } = [];
    public List<OmegaIntelExePatternEntry> ExePatternEntries { get; set; } = [];
    public List<OmegaIntelExeSectionEntry> ExeSectionEntriesList { get; set; } = [];
    public List<OmegaIntelHeroIdCandidate> HeroIdCandidatesList { get; set; } = [];
    public List<OmegaIntelRosterTableCandidate> RosterTableCandidatesList { get; set; } = [];
    public List<OmegaIntelPowerTreeCandidate> PowerTreeCandidatesList { get; set; } = [];
    public List<OmegaIntelStringTableEntry> StringTableEntriesList { get; set; } = [];
    public List<OmegaIntelFunctionSignatureEntry> FunctionSignatureEntriesList { get; set; } = [];
    public List<OmegaIntelValidationIssue> ValidationIssuesList { get; set; } = [];
    public List<OmegaIntelGraphNode> Nodes { get; set; } = [];
    public List<OmegaIntelGraphEdge> Edges { get; set; } = [];

    [JsonIgnore]
    public string ReportStamp => StartedUtc == default ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") : StartedUtc.ToString("yyyyMMdd_HHmmss");
}

public sealed class OmegaIntelFileRecord
{
    public string Path { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public OmegaIntelFileKind Kind { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public string MagicBytes { get; set; } = string.Empty;
    public double Entropy { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public IReadOnlyList<OmegaIntelInsight> Insights { get; set; } = [];
}

public sealed class OmegaIntelInsight
{
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public sealed class OmegaIntelGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public string? Description { get; set; }
    public double Weight { get; set; }
}

public sealed class OmegaIntelGraphEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class OmegaIntelDirectoryRecord
{
    public string Path { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public int UpkCount { get; set; }
    public int TfcCount { get; set; }
    public int TextureCount { get; set; }
    public int MeshCount { get; set; }
    public int AnimationCount { get; set; }
}

public sealed class OmegaIntelTfcMapEntry
{
    public string TextureName { get; set; } = string.Empty;
    public string CacheName { get; set; } = string.Empty;
    public string? TfcPath { get; set; }
    public string? ManifestPath { get; set; }
    public bool CacheFileExists { get; set; }
    public string? TextureFormat { get; set; }
}

public sealed class OmegaIntelUiHeroEntry
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Portrait { get; set; }
    public string? Layout { get; set; }
    public string? HeroId { get; set; }
}

public sealed class OmegaIntelExePatternEntry
{
    public string Path { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string? Evidence { get; set; }
}

public sealed class OmegaIntelHeroIdCandidate
{
    public string Path { get; set; } = string.Empty;
    public string Candidate { get; set; } = string.Empty;
    public string? Evidence { get; set; }
}

public sealed class OmegaIntelRosterTableCandidate
{
    public string Path { get; set; } = string.Empty;
    public string Candidate { get; set; } = string.Empty;
    public string? Evidence { get; set; }
}

public sealed class OmegaIntelPowerTreeCandidate
{
    public string Path { get; set; } = string.Empty;
    public string Candidate { get; set; } = string.Empty;
    public string? Evidence { get; set; }
}

public sealed class OmegaIntelStringTableEntry
{
    public string Path { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public long? Offset { get; set; }
    public string? Category { get; set; }
}

public sealed class OmegaIntelFunctionSignatureEntry
{
    public string Path { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string? Evidence { get; set; }
}

public sealed class OmegaIntelExeSectionEntry
{
    public string Path { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public long VirtualSize { get; set; }
    public long RawSize { get; set; }
    public long VirtualAddress { get; set; }
    public double Entropy { get; set; }
}

public sealed class OmegaIntelValidationIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Details { get; set; }
}

public sealed class OmegaIntelReportSnapshot
{
    public string RootPath { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<OmegaIntelFileRecord> Files { get; set; } = [];
    public List<OmegaIntelGraphNode> Nodes { get; set; } = [];
    public List<OmegaIntelGraphEdge> Edges { get; set; } = [];
}

