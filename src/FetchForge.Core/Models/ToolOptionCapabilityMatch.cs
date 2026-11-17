namespace FetchForge.Core.Models;

public sealed record ToolOptionCapabilityMatch(
    ToolOptionDefinition Definition,
    ToolOptionSupportStatus Status,
    string Message);
