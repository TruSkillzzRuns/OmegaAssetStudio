namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class MFLValidationReport
{
    public List<MFLValidationIssue> Issues { get; set; } = [];

    public bool IsValid => Issues.All(issue => issue.Severity != MFLValidationSeverity.Error);

    public int InfoCount => Issues.Count(issue => issue.Severity == MFLValidationSeverity.Info);

    public int WarningCount => Issues.Count(issue => issue.Severity == MFLValidationSeverity.Warning);

    public int ErrorCount => Issues.Count(issue => issue.Severity == MFLValidationSeverity.Error);

    public string SummaryText => $"{ErrorCount} error(s), {WarningCount} warning(s), {InfoCount} info item(s).";
}

