
namespace FetchForge.Core.Models;

public sealed record ToolCheckResult(
    string ToolName,
    bool Found,
    string? ExecutablePath,
    string Message);
