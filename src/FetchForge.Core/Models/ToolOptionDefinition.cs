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
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tool that owns the option, for example "yt-dlp" or "ffmpeg".
    /// </summary>
    public required string Tool { get; init; }

    /// <summary>
    /// Human-readable label for configuration interfaces.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Explains the effect of the option.
    /// </summary>
    public required string Description { get; init; }

    public required ToolOptionValueType ValueType { get; init; }

    /// <summary>
    /// Default value represented as a JSON-compatible CLR value.
    /// Null means no application default is specified.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Optional grouping label for configuration interfaces.
    /// </summary>
    public string Category { get; init; } = "General";

    /// <summary>
    /// Optional command-line switch associated with this option.
    /// Null indicates an application-level option or an option that
    /// requires custom command-generation logic.
    /// </summary>
    public string? CommandLineArgument { get; init; }

    /// <summary>
    /// Whether the option can be configured by the user.
    /// </summary>
    public bool IsAdvanced { get; init; }

    /// <summary>
    /// Whether the option is enabled by default when applicable.
    /// </summary>
    public bool IsEnabledByDefault { get; init; } = true;

    /// <summary>
    /// Optional inclusive numeric lower bound.
    /// </summary>
    public double? Minimum { get; init; }

    /// <summary>
    /// Optional inclusive numeric upper bound.
    /// </summary>
    public double? Maximum { get; init; }

    /// <summary>
    /// Allowed values for enumeration options.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; }
        = Array.Empty<string>();

    /// <summary>
    /// Validates the metadata definition itself.
    /// Value validation belongs in a separate configuration validator.
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
    }
}
