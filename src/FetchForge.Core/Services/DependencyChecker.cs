using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class DependencyChecker
{
    private readonly ToolCapabilityDiscovery _discovery;
    private readonly ToolOptionRegistry _registry;

    public DependencyChecker()
        : this(new ToolCapabilityDiscovery(), new ToolOptionRegistry())
    {
    }

    public DependencyChecker(
        ToolCapabilityDiscovery discovery,
        ToolOptionRegistry registry)
    {
        _discovery = discovery
            ?? throw new ArgumentNullException(nameof(discovery));
        _registry = registry
            ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<string> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await _discovery.DiscoverAllAsync(cancellationToken);
        var matcher = new ToolOptionCapabilityMatcher(_registry);
        var matches = matcher.Match(results);

        var lines = new List<string>();

        lines.AddRange(results.Select(FormatResult));
        lines.Add("");
        lines.Add("Configured option support:");

        foreach (var match in matches.OrderBy(m => m.Definition.Tool)
                                     .ThenBy(m => m.Definition.Id))
        {
            lines.Add(
                $"[{match.Status}] {match.Definition.Id}: {match.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatResult(ToolCapabilityResult result)
    {
        var status = result.Found ? "FOUND" : "NOT AVAILABLE";
        var version = string.IsNullOrWhiteSpace(result.Version)
            ? string.Empty
            : $" ({result.Version})";
        var path = string.IsNullOrWhiteSpace(result.ExecutablePath)
            ? string.Empty
            : $" [{result.ExecutablePath}]";
        var options = result.Found
            ? $" Discovered {result.SupportedOptions.Count} option names."
            : string.Empty;

        return $"{result.ToolName}: {status}{version}{path} — " +
               $"{result.Message}{options}";
    }
}
