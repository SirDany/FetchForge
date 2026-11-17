namespace FetchForge.Core.Models;

/// <summary>
/// One option as advertised by a tool's own help output.
/// </summary>
/// <param name="Tool">Owning tool.</param>
/// <param name="Switch">Primary switch, e.g. "--format" or "-af".</param>
/// <param name="Aliases">Alternative switches, e.g. "-f".</param>
/// <param name="ValuePlaceholder">Value hint, or null for flags.</param>
/// <param name="Description">Help text, joined onto one line.</param>
/// <param name="Section">Help section heading.</param>
public sealed record HelpOptionInfo(
    string Tool,
    string Switch,
    IReadOnlyList<string> Aliases,
    string? ValuePlaceholder,
    string Description,
    string Section)
{
    public bool TakesValue => ValuePlaceholder is not null;

    public IEnumerable<string> AllSwitches => Aliases.Prepend(Switch);
}

/// <summary>An encoder reported by <c>ffmpeg -encoders</c>.</summary>
public sealed record FfmpegEncoderInfo(
    string Name,
    char MediaType,
    string Description);

public sealed record ToolCapabilityResult(
    string ToolName,
    bool Found,
    string? ExecutablePath,
    string? Version,
    IReadOnlyCollection<string> SupportedOptions,
    string Message)
{
    /// <summary>Help entries with descriptions, when parseable.</summary>
    public IReadOnlyList<HelpOptionInfo> HelpOptions { get; init; } =
        Array.Empty<HelpOptionInfo>();

    /// <summary>FFmpeg only: encoders in the installed build.</summary>
    public IReadOnlyList<FfmpegEncoderInfo> Encoders { get; init; } =
        Array.Empty<FfmpegEncoderInfo>();

    /// <summary>Whether help output was retrieved and parsed.</summary>
    public bool HelpAvailable { get; init; }

    public static ToolCapabilityResult NotFound(
        string toolName,
        string? executablePath,
        string message) =>
        new(toolName, false, executablePath, null,
            Array.Empty<string>(), message);
}
