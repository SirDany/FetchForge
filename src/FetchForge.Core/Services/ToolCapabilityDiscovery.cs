using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.RegularExpressions;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class ToolCapabilityDiscovery
{
    private static readonly Regex OptionPattern = new(
        @"(?<![\w-])--?[A-Za-z0-9][A-Za-z0-9-]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ProcessRunner _processRunner;

    public ToolCapabilityDiscovery()
        : this(new ProcessRunner())
    {
    }

    public ToolCapabilityDiscovery(ProcessRunner processRunner)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<IReadOnlyList<ToolCapabilityResult>> DiscoverAllAsync(
        CancellationToken cancellationToken = default)
    {
        var ytDlp = await DiscoverAsync(
            "yt-dlp",
            ["--version"],
            ["--help"],
            cancellationToken);

        var ffmpeg = await DiscoverAsync(
            "ffmpeg",
            ["-version"],
            ["-hide_banner", "-h", "full"],
            cancellationToken);

        return [ytDlp, ffmpeg];
    }

    public async Task<ToolCapabilityResult> DiscoverAsync(
        string toolName,
        IEnumerable<string> versionArguments,
        IEnumerable<string> helpArguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(versionArguments);
        ArgumentNullException.ThrowIfNull(helpArguments);

        var executablePath = ResolveExecutablePath(toolName);

        try
        {
            var versionResult = await ExecuteCaptureAsync(
                toolName,
                versionArguments,
                cancellationToken);

            var versionOutput = CombineOutput(
                versionResult.StandardOutput,
                versionResult.StandardError);

            if (versionResult.ExitCode != 0)
            {
                return new ToolCapabilityResult(
                    toolName,
                    false,
                    executablePath,
                    null,
                    Array.Empty<string>(),
                    $"Version check failed with exit code {versionResult.ExitCode}: " +
                    FirstLine(versionOutput));
            }

            var version = FirstLine(versionOutput);

            var helpResult = await ExecuteCaptureAsync(
                toolName,
                helpArguments,
                cancellationToken);

            var helpOutput = CombineOutput(
                helpResult.StandardOutput,
                helpResult.StandardError);

            var options = OptionPattern.Matches(helpOutput)
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(option => option, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var message = helpResult.ExitCode == 0
                ? "Version and help information retrieved."
                : $"Version retrieved, but help exited with code {helpResult.ExitCode}.";

            return new ToolCapabilityResult(
                toolName,
                true,
                executablePath,
                version,
                options,
                message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is Win32Exception
            or FileNotFoundException
            or DirectoryNotFoundException
            or InvalidOperationException)
        {
            return new ToolCapabilityResult(
                toolName,
                false,
                executablePath,
                null,
                Array.Empty<string>(),
                $"Unable to run {toolName}: {ex.Message}");
        }
    }

    private async Task<CapturedProcessResult> ExecuteCaptureAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var standardOutput = new ConcurrentQueue<string>();
        var standardError = new ConcurrentQueue<string>();

        var exitCode = await _processRunner.RunAsync(
            executable,
            arguments,
            onOutput: line => standardOutput.Enqueue(line),
            onError: line => standardError.Enqueue(line),
            cancellationToken: cancellationToken);

        return new CapturedProcessResult(
            exitCode,
            string.Join(Environment.NewLine, standardOutput),
            string.Join(Environment.NewLine, standardError));
    }

    private static string CombineOutput(string stdout, string stderr) =>
        string.Join(
            Environment.NewLine,
            new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));

    private static string FirstLine(string output) =>
        output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault() ?? "No version information returned.";

    private static string? ResolveExecutablePath(string executable)
    {
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

            candidates.AddRange(
                extensions.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(extension => executable + extension));
        }

        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(
                    Path.Combine(directory.Trim('"'), candidate));

                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    private sealed record CapturedProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
