using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class ProcessingQueue
{
    private readonly IProcessRunner _processRunner;
    private readonly CommandBuilder _commandBuilder;

    public ProcessingQueue(
        IProcessRunner processRunner,
        CommandBuilder commandBuilder)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
        _commandBuilder = commandBuilder
            ?? throw new ArgumentNullException(nameof(commandBuilder));
    }

    public static bool TryParseFfmpegProgressMicroseconds(
        string line,
        out long microseconds)
    {
        microseconds = 0;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();

        if (trimmed.StartsWith("out_time_us=", StringComparison.Ordinal))
        {
            var value = trimmed["out_time_us=".Length..].Trim();
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) &&
                us >= 0)
            {
                microseconds = us;
                return true;
            }

            return false;
        }

        if (trimmed.StartsWith("out_time=", StringComparison.Ordinal))
        {
            var value = trimmed["out_time=".Length..].Trim();
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts) &&
                ts >= TimeSpan.Zero)
            {
                microseconds = (long)(ts.TotalMilliseconds * 1000);
                return true;
            }
        }

        return false;
    }

    public async Task<TimeSpan?> ProbeDurationAsync(
        string filePath,
        ToolPaths toolPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(toolPaths);

        var output = new ConcurrentQueue<string>();

        try
        {
            var exitCode = await _processRunner.RunAsync(
                toolPaths.Resolve(ToolNames.Ffprobe),
                [
                    "-v", "error",
                    "-show_entries", "format=duration",
                    "-of", "default=noprint_wrappers=1:nokey=1",
                    filePath
                ],
                onOutput: output.Enqueue,
                cancellationToken: cancellationToken);

            if (exitCode != 0)
                return null;

            foreach (var line in output)
            {
                if (double.TryParse(
                        line.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var seconds) &&
                    seconds > 0 &&
                    !double.IsNaN(seconds) &&
                    !double.IsInfinity(seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // ffprobe is optional for progress estimation; fall back gracefully.
        }

        return null;
    }

    public async Task ProcessFilesAsync(
        IReadOnlyList<JobFileItem> items,
        FetchForgeProfile profile,
        string processedDirectory,
        ToolPaths toolPaths,
        IProgress<JobProgressUpdate>? progress = null,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default,
        IEnumerable<ToolCapabilityResult>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(processedDirectory);
        ArgumentNullException.ThrowIfNull(toolPaths);

        if (items.Count == 0)
            return;

        Directory.CreateDirectory(processedDirectory);

        var maxConcurrency = Math.Clamp(
            profile.Ffmpeg.MaxConcurrentJobs,
            1,
            FfmpegProfileSettings.MaxAllowedConcurrentJobs);

        var sampleCommand = _commandBuilder.BuildFfmpegCommand(
            profile,
            items[0].SourcePath,
            "preview",
            toolPaths,
            capabilities,
            forExecution: true);

        if (sampleCommand.HasErrors)
        {
            var errors = string.Join("; ", sampleCommand.Issues
                .Where(i => i.Severity == OptionValidationSeverity.Error)
                .Select(i => i.Message));
            throw new InvalidOperationException($"Invalid FFmpeg configuration: {errors}");
        }

        var extension = _commandBuilder.ResolveOutputExtension(profile);
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var itemProgress = new double[items.Count];

        void ReportOverall(string message, JobFileItem changedItem)
        {
            double average;
            lock (itemProgress)
                average = itemProgress.Average();

            progress?.Report(new JobProgressUpdate(
                JobStage.Processing,
                message,
                50 + (average * 0.5),
                changedItem));
        }

        var tasks = items.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = string.IsNullOrWhiteSpace(item.RelativePath)
                    ? Path.GetFileName(item.SourcePath)
                    : item.RelativePath;
                var relativeDir = Path.GetDirectoryName(relative) ?? string.Empty;
                var fileName = $"{Path.GetFileNameWithoutExtension(relative)}.{extension}";
                var targetDir = string.IsNullOrEmpty(relativeDir)
                    ? processedDirectory
                    : Path.Combine(processedDirectory, relativeDir);
                Directory.CreateDirectory(targetDir);

                var outputPath = Path.Combine(targetDir, fileName);
                item.Status = FileItemStatus.Processing;
                item.ProgressPercent = 0;
                item.StatusText = "Probing...";
                ReportOverall($"Processing {item.Title}...", item);

                item.Duration = await ProbeDurationAsync(
                    item.SourcePath, toolPaths, cancellationToken);

                var command = _commandBuilder.BuildFfmpegCommand(
                    profile,
                    item.SourcePath,
                    outputPath,
                    toolPaths,
                    capabilities,
                    forExecution: true);

                onLog?.Invoke($"FFmpeg [{item.Title}]: {command.FormattedCommandLine}");

                var errorTail = new ConcurrentQueue<string>();
                var sw = Stopwatch.StartNew();

                var exitCode = await _processRunner.RunAsync(
                    command.Executable,
                    command.Arguments,
                    processedDirectory,
                    onOutput: line =>
                    {
                        if (!TryParseFfmpegProgressMicroseconds(line, out var us))
                            return;

                        if (item.Duration is { TotalMilliseconds: > 0 } total)
                        {
                            var pct = Math.Clamp(
                                (us / 1000.0) / total.TotalMilliseconds * 100.0, 0, 99.9);
                            lock (itemProgress)
                                itemProgress[index] = pct;

                            item.ProgressPercent = pct;
                            item.StatusText = $"{pct:0.0}%";
                            ReportOverall($"Processing {item.Title} ({pct:0.0}%)", item);
                        }
                        else
                        {
                            var elapsed = TimeSpan.FromMilliseconds(us / 1000.0);
                            item.StatusText = $"{elapsed:mm\\:ss}";
                            ReportOverall($"Processing {item.Title} ({item.StatusText})", item);
                        }
                    },
                    onError: line =>
                    {
                        errorTail.Enqueue(line);
                        while (errorTail.Count > 12)
                            errorTail.TryDequeue(out _);
                    },
                    cancellationToken);

                sw.Stop();
                item.ProcessingElapsed = sw.Elapsed;

                if (exitCode == 0 && File.Exists(outputPath))
                {
                    item.OutputPath = outputPath;
                    item.ProgressPercent = 100;
                    item.Status = FileItemStatus.Completed;
                    item.StatusText = $"Done ({sw.Elapsed:mm\\:ss\\.f})";
                    lock (itemProgress)
                        itemProgress[index] = 100;

                    ReportOverall($"Processed {item.Title}", item);
                }
                else
                {
                    var detail = errorTail.IsEmpty
                        ? $"FFmpeg exited with code {exitCode}."
                        : string.Join(" | ", errorTail.TakeLast(3));

                    item.Status = FileItemStatus.Failed;
                    item.ErrorMessage = detail;
                    item.StatusText = "Failed";
                    lock (itemProgress)
                        itemProgress[index] = 100;

                    onLog?.Invoke($"FFmpeg failed [{item.Title}]: {detail}");
                    ReportOverall($"Failed: {item.Title}", item);
                }
            }
            catch (OperationCanceledException)
            {
                item.Status = FileItemStatus.Cancelled;
                item.StatusText = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                item.Status = FileItemStatus.Failed;
                item.ErrorMessage = ex.Message;
                item.StatusText = "Failed";
                lock (itemProgress)
                    itemProgress[index] = 100;

                onLog?.Invoke($"FFmpeg error [{item.Title}]: {ex.Message}");
                ReportOverall($"Failed: {item.Title}", item);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
