namespace FetchForge.Core.Models;

public sealed record ToolCapabilityResult(
    string ToolName,
    bool Found,
    string? ExecutablePath,
    string? Version,
    IReadOnlyCollection<string> SupportedOptions,
    string Message);
