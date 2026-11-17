using System.Diagnostics;
using System.Text;

namespace FetchForge.Core.Services;

/// <summary>Starts external tools. Abstracted for testing.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="executable"/> with an argument list (never a
    /// shell string) and streams output lines to the callbacks.
    /// On cancellation the whole process tree is killed and an
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    Task<int> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory =
                workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        // Make Python-based tools (yt-dlp) emit UTF-8 when redirected,
        // so non-ASCII titles and paths survive.
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onOutput?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onError?.Invoke(e.Data);
        };

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // Let the stream readers drain, but never hang forever.
            using var drain = new CancellationTokenSource(
                TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(drain.Token);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        return process.ExitCode;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied or exiting; nothing more we can do.
        }
    }
}
