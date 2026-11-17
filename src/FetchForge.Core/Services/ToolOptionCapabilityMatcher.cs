using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class ToolOptionCapabilityMatcher
{
    private readonly ToolOptionRegistry _registry;

    public ToolOptionCapabilityMatcher(ToolOptionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<ToolOptionCapabilityMatch> Match(
        IEnumerable<ToolCapabilityResult> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var byTool = capabilities
            .GroupBy(c => c.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return _registry.All
            .Select(definition => Match(definition, byTool))
            .ToArray();
    }

    public ToolOptionCapabilityMatch Match(
        ToolOptionDefinition definition,
        IEnumerable<ToolCapabilityResult> capabilities)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(capabilities);

        var byTool = capabilities
            .GroupBy(c => c.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return Match(definition, byTool);
    }

    private static ToolOptionCapabilityMatch Match(
        ToolOptionDefinition definition,
        IReadOnlyDictionary<string, ToolCapabilityResult> byTool)
    {
        if (!byTool.TryGetValue(definition.Tool, out var capability) ||
            !capability.Found)
        {
            return new(
                definition,
                ToolOptionSupportStatus.Unverified,
                "The required tool was not found; support could not be checked.");
        }

        if (string.IsNullOrWhiteSpace(definition.CommandLineArgument))
        {
            return new(
                definition,
                ToolOptionSupportStatus.Unverified,
                "This option has no direct command-line mapping.");
        }

        if (capability.SupportedOptions is null ||
            capability.SupportedOptions.Count == 0 ||
            capability.Message.Contains("help exited", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                definition,
                ToolOptionSupportStatus.Unverified,
                "The tool's help output was unavailable or contained no discoverable options.");
        }

        var expected = NormalizeSwitch(definition.CommandLineArgument);
        var supported = capability.SupportedOptions
            .Select(NormalizeSwitch)
            .Any(option => string.Equals(
                option, expected, StringComparison.OrdinalIgnoreCase));

        return supported
            ? new(
                definition,
                ToolOptionSupportStatus.Supported,
                $"The installed tool advertises '{definition.CommandLineArgument}'.")
            : new(
                definition,
                ToolOptionSupportStatus.Unsupported,
                $"The installed tool did not advertise '{definition.CommandLineArgument}'.");
    }

    private static string NormalizeSwitch(string option)
    {
        var colon = option.IndexOf(':');
        return colon >= 0 ? option[..colon] : option;
    }
}
