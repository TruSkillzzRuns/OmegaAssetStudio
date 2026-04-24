namespace OmegaAssetStudio.Retargeting;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Rule,
    string Message,
    string? BoneName = null);

public sealed class ValidationResult
{
    public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
    public List<ValidationIssue> Issues { get; } = [];
    public int SourceBoneCount { get; set; }
    public int TargetBoneCount { get; set; }

    public int ErrorCount => Issues.Count(issue => issue.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
    public int InfoCount => Issues.Count(issue => issue.Severity == ValidationSeverity.Info);
}

