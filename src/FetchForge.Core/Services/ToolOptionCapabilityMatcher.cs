using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Checks registered option definitions against the options the
/// installed tools advertise.
/// </summary>
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

        var byTool = ToLookup(capabilities);

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

        return Match(definition, ToLookup(capabilities));
    }

    private static Dictionary<string, ToolCapabilityResult> ToLookup(
        IEnumerable<ToolCapabilityResult> capabilities) =>
        capabilities
            .GroupBy(c => c.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static ToolOptionCapabilityMatch Match(
        ToolOptionDefinition definition,
        IReadOnlyDictionary<string, ToolCapabilityResult> byTool)
    {
        var switches = definition.EmittedSwitches.ToArray();

        if (switches.Length == 0)
        {
            return new(
                definition,
                ToolOptionSupportStatus.NotApplicable,
                "FetchForge setting; no command-line switch to check.");
        }

        if (!byTool.TryGetValue(definition.Tool, out var capability) ||
            !capability.Found)
        {
            return new(
                definition,
                ToolOptionSupportStatus.Unverified,
                "The required tool was not found; support could not be checked.");
        }

        if (!capability.HelpAvailable || capability.SupportedOptions.Count == 0)
        {
            return new(
                definition,
                ToolOptionSupportStatus.Unverified,
                "The tool's help output was unavailable or contained no discoverable options.");
        }

        var advertised = new HashSet<string>(
            capability.SupportedOptions.Select(NormalizeSwitch),
            StringComparer.Ordinal);

        var missing = switches
            .Where(s => !advertised.Contains(NormalizeSwitch(s)))
            .ToArray();

        return missing.Length == 0
            ? new(
                definition,
                ToolOptionSupportStatus.Supported,
                $"{capability.ToolName} {capability.Version} supports " +
                $"{string.Join(", ", switches)}.")
            : new(
                definition,
                ToolOptionSupportStatus.Unsupported,
                $"{capability.ToolName} {capability.Version} does not advertise " +
                $"{string.Join(", ", missing)}.");
    }

    /// <summary>"-c:a" → "-c"; "-q:a" → "-q".</summary>
    public static string NormalizeSwitch(string option)
    {
        var colon = option.IndexOf(':');
        var trimmed = colon > 0 ? option[..colon] : option;

        var equals = trimmed.IndexOf('=');
        return equals > 0 ? trimmed[..equals] : trimmed;
    }
}
