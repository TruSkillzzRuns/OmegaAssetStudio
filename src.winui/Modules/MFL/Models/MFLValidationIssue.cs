namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public enum MFLValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed class MFLValidationIssue
{
    public MFLValidationSeverity Severity { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
}

