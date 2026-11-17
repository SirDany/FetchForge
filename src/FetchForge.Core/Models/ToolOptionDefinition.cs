namespace FetchForge.Core.Models;

/// <summary>
/// FetchForge-owned metadata describing a configurable tool option.
/// This definition describes the option independently of whether the
/// installed tool version supports it.
/// </summary>
public sealed record ToolOptionDefinition
{
    /// <summary>
    /// Stable, application-owned identifier, such as "yt-dlp.format".
    /// Used as the key in saved profiles.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tool that owns the option: <see cref="ToolNames.YtDlp"/> or
    /// <see cref="ToolNames.Ffmpeg"/>.
    /// </summary>
    public required string Tool { get; init; }

    /// <summary>Human-readable label for configuration interfaces.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Explains the effect of the option.</summary>
    public required string Description { get; init; }

    public required ToolOptionValueType ValueType { get; init; }

    /// <summary>
    /// Default value as a JSON-compatible CLR value (bool, int, double,
    /// string). Null means the option is not emitted unless set.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>Grouping label for configuration interfaces.</summary>
    public string Category { get; init; } = "General";

    /// <summary>
    /// Primary command-line switch. Used to emit value options and to
    /// check support against the installed tool. Null indicates an
    /// application-level option without a direct switch.
    /// </summary>
    public string? CommandLineArgument { get; init; }

    /// <summary>
    /// Arguments emitted when a boolean option is true.
    /// Defaults to <see cref="CommandLineArgument"/> alone.
    /// </summary>
    public IReadOnlyList<string>? TrueArguments { get; init; }

    /// <summary>
    /// Arguments emitted when a boolean option is false.
    /// Defaults to nothing.
    /// </summary>
    public IReadOnlyList<string>? FalseArguments { get; init; }

    /// <summary>Example or hint shown in empty text inputs.</summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// Whether the option is hidden behind an "advanced" toggle.
    /// </summary>
    public bool IsAdvanced { get; init; }

    /// <summary>
    /// Restricts when the option is applied in relation to the FFmpeg
    /// post-processing stage.
    /// </summary>
    public OptionApplicability AppliesWhen { get; init; } =
        OptionApplicability.Always;

    /// <summary>Optional inclusive numeric lower bound.</summary>
    public double? Minimum { get; init; }

    /// <summary>Optional inclusive numeric upper bound.</summary>
    public double? Maximum { get; init; }

    /// <summary>Allowed values for enumeration options.</summary>
    public IReadOnlyList<string> AllowedValues { get; init; }
        = Array.Empty<string>();

    /// <summary>
    /// For enumeration values that name an FFmpeg encoder: validate the
    /// selected value against the encoders reported by the installed
    /// FFmpeg build.
    /// </summary>
    public bool ValueIsFfmpegEncoder { get; init; }

    /// <summary>
    /// Effective arguments emitted when the boolean option is true.
    /// </summary>
    public IReadOnlyList<string> EffectiveTrueArguments =>
        TrueArguments
        ?? (CommandLineArgument is null
            ? Array.Empty<string>()
            : [CommandLineArgument]);

    /// <summary>
    /// Effective arguments emitted when the boolean option is false.
    /// </summary>
    public IReadOnlyList<string> EffectiveFalseArguments =>
        FalseArguments ?? Array.Empty<string>();

    /// <summary>
    /// Every switch this option can emit, for capability matching.
    /// </summary>
    public IEnumerable<string> EmittedSwitches
    {
        get
        {
            if (ValueType == ToolOptionValueType.Boolean)
            {
                return EffectiveTrueArguments
                    .Concat(EffectiveFalseArguments)
                    .Where(argument => argument.StartsWith('-'))
                    .Distinct(StringComparer.Ordinal);
            }

            return CommandLineArgument is null
                ? Array.Empty<string>()
                : [CommandLineArgument];
        }
    }

    /// <summary>
    /// Validates the metadata definition itself.
    /// Value validation is handled by <c>OptionValueValidator</c>.
    /// </summary>
    public void ValidateDefinition()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException(
                "An option identifier is required.");

        if (string.IsNullOrWhiteSpace(Tool))
            throw new InvalidOperationException(
                $"Option '{Id}' must specify its owning tool.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new InvalidOperationException(
                $"Option '{Id}' must specify a display name.");

        if (string.IsNullOrWhiteSpace(Description))
            throw new InvalidOperationException(
                $"Option '{Id}' must specify a description.");

        if (Minimum.HasValue &&
            Maximum.HasValue &&
            Minimum.Value > Maximum.Value)
        {
            throw new InvalidOperationException(
                $"Option '{Id}' has a minimum greater than its maximum.");
        }

        if (ValueType == ToolOptionValueType.Enumeration &&
            AllowedValues.Count == 0)
        {
            throw new InvalidOperationException(
                $"Enumeration option '{Id}' must define allowed values.");
        }

        if (ValueType != ToolOptionValueType.Enumeration &&
            AllowedValues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Non-enumeration option '{Id}' cannot define allowed values.");
        }

        if (ValueType != ToolOptionValueType.Boolean &&
            (TrueArguments is not null || FalseArguments is not null))
        {
            throw new InvalidOperationException(
                $"Only boolean options can define true/false arguments ('{Id}').");
        }

        if (DefaultValue is not null &&
            OptionValueFormatter.Format(DefaultValue) is { } text &&
            ValueType == ToolOptionValueType.Enumeration &&
            !AllowedValues.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Default value of '{Id}' is not one of its allowed values.");
        }
    }
}
