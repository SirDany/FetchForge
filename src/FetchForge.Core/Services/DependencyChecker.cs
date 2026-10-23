
using System.Diagnostics;

namespace FetchForge.Core.Services;

public sealed class DependencyChecker
{
    public async Task<string> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var ytDlp = await CheckExecutableAsync(
            "yt-dlp", "--version", cancellationToken);

        var ffmpeg = await CheckExecutableAsync(
            "ffmpeg", "-version", cancellationToken);

        return string.Join(
            Environment.NewLine,
            ytDlp,
            ffmpeg);
    }

    private static async Task<string> CheckExecutableAsync(
        string executable,
        string argument,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.StartInfo.ArgumentList.Add(argument);

            process.Start();

            var stdout = process.StandardOutput.ReadToEndAsync(
                cancellationToken);

            var stderr = process.StandardError.ReadToEndAsync(
                cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = (await stdout + " " + await stderr).Trim();
            var firstLine = output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "No version information returned.";

            return $"{executable}: " +
                (process.ExitCode == 0 ? "FOUND" : "ERROR") +
                $" — {firstLine}";
        }
        catch (Exception ex) when (
            ex is System.ComponentModel.Win32Exception
            or FileNotFoundException)
        {
            return $"{executable}: NOT FOUND on PATH";
        }
    }
}
