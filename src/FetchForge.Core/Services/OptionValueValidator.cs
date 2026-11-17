using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Validates configured option values in a profile against registry
/// constraints and discovered tool capabilities.
/// </summary>
public sealed class OptionValueValidator
{
    private readonly ToolOptionRegistry _registry;

    public OptionValueValidator(ToolOptionRegistry registry)
    {
        _registry = registry
            ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<OptionValidationIssue> ValidateProfile(
        FetchForgeProfile profile,
        IEnumerable<ToolCapabilityResult>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var capabilityList = capabilities?.ToArray();
        var matcher = capabilityList is null
            ? null
            : new ToolOptionCapabilityMatcher(_registry);
        var ffmpegCap = capabilityList?.FirstOrDefault(c =>
            string.Equals(c.ToolName, ToolNames.Ffmpeg, StringComparison.OrdinalIgnoreCase));

        var issues = new List<OptionValidationIssue>();

        foreach (var definition in _registry.All)
        {
            if (definition.Tool == ToolNames.Ffmpeg && !profile.Ffmpeg.Enabled)
                continue;

            if (definition.AppliesWhen == OptionApplicability.WhenProcessingDisabled &&
                profile.Ffmpeg.Enabled)
                continue;

            var rawValue = profile.GetValue(definition);

            if (string.IsNullOrWhiteSpace(rawValue) &&
                !profile.IsOverridden(definition))
            {
                continue;
            }

            ValidateTypeAndBounds(definition, rawValue, issues);

            if (definition.ValueIsFfmpegEncoder &&
                !string.IsNullOrWhiteSpace(rawValue) &&
                ffmpegCap is { Found: true, Encoders.Count: > 0 })
            {
                var supportedEncoder = ffmpegCap.Encoders.Any(e =>
                    e.MediaType == 'A' &&
                    string.Equals(e.Name, rawValue.Trim(), StringComparison.OrdinalIgnoreCase));

                if (!supportedEncoder)
                {
                    issues.Add(new OptionValidationIssue(
                        definition.Tool,
                        definition.Id,
                        OptionValidationSeverity.Warning,
                        $"The installed FFmpeg build does not advertise audio encoder '{rawValue.Trim()}'."));
                }
            }

            if (matcher is not null &&
                capabilityList is not null &&
                (profile.IsOverridden(definition) || !string.IsNullOrWhiteSpace(rawValue)))
            {
                var match = matcher.Match(definition, capabilityList);
                if (match.Status == ToolOptionSupportStatus.Unsupported)
                {
                    issues.Add(new OptionValidationIssue(
                        definition.Tool,
                        definition.Id,
                        OptionValidationSeverity.Warning,
                        match.Message));
                }
            }
        }

        return issues;
    }

    private static void ValidateTypeAndBounds(
        ToolOptionDefinition definition,
        string? rawValue,
        List<OptionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        var trimmed = rawValue.Trim();

        switch (definition.ValueType)
        {
            case ToolOptionValueType.Boolean:
                if (!OptionValueFormatter.TryParseBoolean(trimmed, out _))
                {
                    issues.Add(new OptionValidationIssue(
                        definition.Tool,
                        definition.Id,
                        OptionValidationSeverity.Error,
                        $"Option '{definition.DisplayName}' expects a boolean value (true/false), got '{rawValue}'."));
                }
                break;

            case ToolOptionValueType.Integer:
                if (!OptionValueFormatter.TryParseInteger(trimmed, out var intVal))
                {
                    issues.Add(new OptionValidationIssue(
                        definition.Tool,
                        definition.Id,
                        OptionValidationSeverity.Error,
                        $"Option '{definition.DisplayName}' expects an integer, got '{rawValue}'."));
                    return;
                }

                CheckRange(definition, intVal, issues);
                break;

            case ToolOptionValueType.Decimal:
                if (!OptionValueFormatter.TryParseDecimal(trimmed, out var decVal) ||
                    double.IsNaN(decVal) ||
                    double.IsInfinity(decVal))
                {
                    issues.Add(new OptionValidationIssue(
                        definition.Tool,
                        definition.Id,
                        OptionValidationSeverity.Error,
                        $"Option '{definition.DisplayName}' expects a decimal number, got '{rawValue}'."));
                    return;
                }

                CheckRange(definition, decVal, issues);
                break;

            case ToolOptionValueType.Enumeration:
                if (!definition.AllowedValues.Contains(
                        trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add(new OptionValidationIssue(
                        definition.Tool,
                        definition.Id,
                        OptionValidationSeverity.Error,
                        $"Option '{definition.DisplayName}' value '{rawValue}' is not one of: {string.Join(", ", definition.AllowedValues)}."));
                }
                break;
        }
    }

    private static void CheckRange(
        ToolOptionDefinition definition,
        double value,
        List<OptionValidationIssue> issues)
    {
        if (definition.Minimum.HasValue && value < definition.Minimum.Value)
        {
            issues.Add(new OptionValidationIssue(
                definition.Tool,
                definition.Id,
                OptionValidationSeverity.Error,
                $"Option '{definition.DisplayName}' must be at least {definition.Minimum.Value} (got {value})."));
        }

        if (definition.Maximum.HasValue && value > definition.Maximum.Value)
        {
            issues.Add(new OptionValidationIssue(
                definition.Tool,
                definition.Id,
                OptionValidationSeverity.Error,
                $"Option '{definition.DisplayName}' must be at most {definition.Maximum.Value} (got {value})."));
        }
    }
}
