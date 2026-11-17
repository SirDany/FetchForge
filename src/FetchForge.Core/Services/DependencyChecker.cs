using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed record DependencyCheckReport(
    IReadOnlyList<ToolCapabilityResult> Capabilities,
    IReadOnlyList<ToolOptionCapabilityMatch> Matches,
    string SummaryText);

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

    public Task<string> CheckAsync(
        CancellationToken cancellationToken = default) =>
        CheckAsync(paths: null, cancellationToken);

    public async Task<string> CheckAsync(
        ToolPaths? paths,
        CancellationToken cancellationToken = default)
    {
        var report = await DiscoverAndMatchAsync(paths, cancellationToken);
        return report.SummaryText;
    }

    public async Task<DependencyCheckReport> DiscoverAndMatchAsync(
        ToolPaths? paths = null,
        CancellationToken cancellationToken = default)
    {
        var results = await _discovery.DiscoverAllAsync(paths, cancellationToken);
        var matcher = new ToolOptionCapabilityMatcher(_registry);
        var matches = matcher.Match(results);

        var lines = new List<string>();
        lines.AddRange(results.Select(FormatResult));
        lines.Add(string.Empty);
        lines.Add("Configured option support:");

        foreach (var match in matches
                     .OrderBy(m => m.Definition.Tool)
                     .ThenBy(m => m.Definition.Id))
        {
            lines.Add(
                $"[{match.Status}] {match.Definition.Id}: {match.Message}");
        }

        return new DependencyCheckReport(
            results,
            matches,
            string.Join(Environment.NewLine, lines));
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
        var options = result.Found && result.HelpOptions.Count > 0
            ? $" Discovered {result.HelpOptions.Count} help entries ({result.SupportedOptions.Count} switches)."
            : string.Empty;

        return $"{result.ToolName}: {status}{version}{path} — " +
               $"{result.Message}{options}";
    }
}
