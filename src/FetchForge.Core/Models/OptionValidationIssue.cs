namespace FetchForge.Core.Models;

public enum OptionValidationSeverity
{
    Warning,
    Error
}

public sealed record OptionValidationIssue(
    string Tool,
    string? OptionId,
    OptionValidationSeverity Severity,
    string Message);
