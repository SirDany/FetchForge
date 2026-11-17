using System.Collections.Concurrent;
using System.ComponentModel;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Locates yt-dlp, FFmpeg and ffprobe, reads their versions and parses
/// the options they advertise.
/// </summary>
public sealed class ToolCapabilityDiscovery
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _processRunner;

    public ToolCapabilityDiscovery()
        : this(new ProcessRunner())
    {
    }

    public ToolCapabilityDiscovery(IProcessRunner processRunner)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<IReadOnlyList<ToolCapabilityResult>> DiscoverAllAsync(
        ToolPaths? paths = null,
        CancellationToken cancellationToken = default)
    {
        paths ??= new ToolPaths();

        var ytDlp = DiscoverYtDlpAsync(
            paths.Resolve(ToolNames.YtDlp), cancellationToken);
        var ffmpeg = DiscoverFfmpegAsync(
            paths.Resolve(ToolNames.Ffmpeg), cancellationToken);
        var ffprobe = DiscoverAsync(
            ToolNames.Ffprobe,
            paths.Resolve(ToolNames.Ffprobe),
            ["-version"],
            helpArguments: null,
            parseHelp: null,
            cancellationToken);

        return await Task.WhenAll(ytDlp, ffmpeg, ffprobe);
    }

    public Task<ToolCapabilityResult> DiscoverYtDlpAsync(
        string executable,
        CancellationToken cancellationToken = default) =>
        DiscoverAsync(
            ToolNames.YtDlp,
            executable,
            ["--version"],
            ["--help"],
            ToolHelpParser.ParseYtDlpHelp,
            cancellationToken);

    public async Task<ToolCapabilityResult> DiscoverFfmpegAsync(
        string executable,
        CancellationToken cancellationToken = default)
    {
        var result = await DiscoverAsync(
            ToolNames.Ffmpeg,
            executable,
            ["-hide_banner", "-version"],
            ["-hide_banner", "-h", "long"],
            ToolHelpParser.ParseFfmpegHelp,
            cancellationToken);

        if (!result.Found)
            return result;

        try
        {
            var encoders = await CaptureAsync(
                executable, ["-hide_banner", "-encoders"], cancellationToken);

            return result with
            {
                Encoders = encoders.ExitCode == 0
                    ? ToolHelpParser.ParseFfmpegEncoders(encoders.Combined)
                    : Array.Empty<FfmpegEncoderInfo>()
            };
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            return result;
        }
    }

    private async Task<ToolCapabilityResult> DiscoverAsync(
        string toolName,
        string executable,
        IReadOnlyList<string> versionArguments,
        IReadOnlyList<string>? helpArguments,
        Func<string, IReadOnlyList<HelpOptionInfo>>? parseHelp,
        CancellationToken cancellationToken)
    {
        var executablePath = ResolveExecutablePath(executable);

        try
        {
            var versionResult = await CaptureAsync(
                executable, versionArguments, cancellationToken);

            if (versionResult.ExitCode != 0)
            {
                return ToolCapabilityResult.NotFound(
                    toolName,
                    executablePath,
                    $"Version check failed with exit code {versionResult.ExitCode}: " +
                    FirstLine(versionResult.Combined));
            }

            var version = ShortVersion(toolName, FirstLine(versionResult.Combined));

            if (helpArguments is null || parseHelp is null)
            {
                return new ToolCapabilityResult(
                    toolName, true, executablePath, version,
                    Array.Empty<string>(), "Version information retrieved.");
            }

            var helpResult = await CaptureAsync(
                executable, helpArguments, cancellationToken);

            var helpOptions = helpResult.ExitCode == 0
                ? parseHelp(helpResult.StandardOutput.Length > 0
                    ? helpResult.StandardOutput
                    : helpResult.Combined)
                : Array.Empty<HelpOptionInfo>();

            var switches = helpOptions
                .SelectMany(option => option.AllSwitches)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            var message = helpResult.ExitCode == 0
                ? $"Version and help retrieved ({helpOptions.Count} options)."
                : $"Version retrieved, but help exited with code {helpResult.ExitCode}.";

            return new ToolCapabilityResult(
                toolName, true, executablePath, version, switches, message)
            {
                HelpOptions = helpOptions,
                HelpAvailable = helpResult.ExitCode == 0 && helpOptions.Count > 0
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ToolCapabilityResult.NotFound(
                toolName, executablePath,
                $"{toolName} did not respond within {ToolTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            return ToolCapabilityResult.NotFound(
                toolName, executablePath,
                executablePath is null && !Path.IsPathRooted(executable)
                    ? $"Not found on PATH ({ex.Message})."
                    : $"Unable to run {executable}: {ex.Message}");
        }
    }

    private async Task<CapturedProcessResult> CaptureAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToolTimeout);

        var standardOutput = new ConcurrentQueue<string>();
        var standardError = new ConcurrentQueue<string>();

        var exitCode = await _processRunner.RunAsync(
            executable,
            arguments,
            onOutput: standardOutput.Enqueue,
            onError: standardError.Enqueue,
            cancellationToken: timeout.Token);

        return new CapturedProcessResult(
            exitCode,
            string.Join('\n', standardOutput),
            string.Join('\n', standardError));
    }

    private static bool IsLaunchFailure(Exception ex) =>
        ex is Win32Exception
            or FileNotFoundException
            or DirectoryNotFoundException
            or InvalidOperationException
            or UnauthorizedAccessException;

    private static string FirstLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault() ?? "No version information returned.";

    /// <summary>"ffmpeg version 7.0 Copyright ..." → "7.0".</summary>
    private static string ShortVersion(string toolName, string firstLine)
    {
        var prefix = $"{toolName} version ";
        if (!firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return firstLine;

        var rest = firstLine[prefix.Length..];
        var space = rest.IndexOf(' ');
        return space > 0 ? rest[..space] : rest;
    }

    public static string? ResolveExecutablePath(string executable)
    {
        if (Path.IsPathRooted(executable))
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;

        var pathValue = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        var candidates = new List<string> { executable };

        if (OperatingSystem.IsWindows()
            && string.IsNullOrEmpty(Path.GetExtension(executable)))
        {
            var extensions = Environment.GetEnvironmentVariable("PATHEXT");

            if (string.IsNullOrWhiteSpace(extensions))
                extensions = ".COM;.EXE;.BAT;.CMD";

            candidates.InsertRange(0,
                extensions.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(extension => executable + extension));
        }

        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(
                        Path.Combine(directory.Trim('"'), candidate));

                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (Exception ex) when (
                    ex is ArgumentException or NotSupportedException
                        or PathTooLongException)
                {
                    // Malformed PATH entry; skip it.
                }
            }
        }

        return null;
    }

    private sealed record CapturedProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string Combined => string.Join(
            '\n',
            new[] { StandardOutput, StandardError }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }
}
