using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class JobOrchestrator
{
    private readonly DownloadService _downloadService;
    private readonly ProcessingQueue _processingQueue;
    private readonly CommandBuilder _commandBuilder;

    public JobOrchestrator()
        : this(new ProcessRunner(), new ToolOptionRegistry())
    {
    }

    public JobOrchestrator(
        IProcessRunner processRunner,
        ToolOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(registry);

        _commandBuilder = new CommandBuilder(registry);
        _downloadService = new DownloadService(processRunner, _commandBuilder);
        _processingQueue = new ProcessingQueue(processRunner, _commandBuilder);
    }

    public JobOrchestrator(
        DownloadService downloadService,
        ProcessingQueue processingQueue,
        CommandBuilder commandBuilder)
    {
        _downloadService = downloadService
            ?? throw new ArgumentNullException(nameof(downloadService));
        _processingQueue = processingQueue
            ?? throw new ArgumentNullException(nameof(processingQueue));
        _commandBuilder = commandBuilder
            ?? throw new ArgumentNullException(nameof(commandBuilder));
    }

    public async Task<JobReport> RunJobAsync(
        FetchForgeProfile profile,
        AppSettings settings,
        string url,
        IProgress<JobProgressUpdate>? progress = null,
        Action<string>? onLog = null,
        IEnumerable<ToolCapabilityResult>? capabilities = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var startedAt = DateTimeOffset.UtcNow;
        var outputDirectory = Path.GetFullPath(settings.OutputDirectory);
        var tempRoot = string.IsNullOrWhiteSpace(settings.TemporaryDirectory)
            ? Path.Combine(Path.GetTempPath(), "FetchForge")
            : settings.TemporaryDirectory;

        var jobTempDir = Path.Combine(
            tempRoot,
            $"job-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32]);
        var downloadDir = Path.Combine(jobTempDir, "download");
        var processedDir = Path.Combine(jobTempDir, "processed");

        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(processedDir);
        Directory.CreateDirectory(outputDirectory);

        var items = new List<JobFileItem>();

        try
        {
            var downloaded = await _downloadService.DownloadAsync(
                profile,
                url,
                downloadDir,
                settings.ToolPaths,
                progress,
                onLog,
                cancellationToken,
                capabilities);

            items.AddRange(downloaded);

            if (items.Count == 0)
            {
                return new JobReport(
                    JobStage.Failed,
                    url,
                    outputDirectory,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    items,
                    "yt-dlp completed without producing any files.");
            }

            if (profile.Ffmpeg.Enabled)
            {
                progress?.Report(new JobProgressUpdate(
                    JobStage.Processing,
                    $"Processing {items.Count} file(s) with FFmpeg (max {profile.Ffmpeg.MaxConcurrentJobs} concurrent)...",
                    50));

                await _processingQueue.ProcessFilesAsync(
                    items,
                    profile,
                    processedDir,
                    settings.ToolPaths,
                    progress,
                    onLog,
                    cancellationToken,
                    capabilities);
            }

            MoveOutputsToDestination(
                items,
                profile,
                outputDirectory,
                onLog);

            var allFailed = items.All(i => i.Status == FileItemStatus.Failed);
            var finalStage = allFailed ? JobStage.Failed : JobStage.Completed;
            var message = allFailed
                ? "All files failed during processing."
                : $"Completed {items.Count(i => i.Status == FileItemStatus.Completed)} of {items.Count} file(s).";

            progress?.Report(new JobProgressUpdate(finalStage, message, 100));

            return new JobReport(
                finalStage,
                url,
                outputDirectory,
                startedAt,
                DateTimeOffset.UtcNow,
                items);
        }
        catch (OperationCanceledException)
        {
            foreach (var item in items.Where(i =>
                         i.Status is not FileItemStatus.Completed and not FileItemStatus.Failed))
            {
                item.Status = FileItemStatus.Cancelled;
                item.StatusText = "Cancelled";
            }

            progress?.Report(new JobProgressUpdate(
                JobStage.Cancelled, "Job cancelled.", 0));

            return new JobReport(
                JobStage.Cancelled,
                url,
                outputDirectory,
                startedAt,
                DateTimeOffset.UtcNow,
                items,
                "Cancelled by user.");
        }
        catch (Exception ex)
        {
            foreach (var item in items.Where(i =>
                         i.Status is not FileItemStatus.Completed and not FileItemStatus.Failed))
            {
                item.Status = FileItemStatus.Failed;
                item.ErrorMessage ??= ex.Message;
                item.StatusText = "Failed";
            }

            progress?.Report(new JobProgressUpdate(
                JobStage.Failed, $"Job failed: {ex.Message}", 0));

            return new JobReport(
                JobStage.Failed,
                url,
                outputDirectory,
                startedAt,
                DateTimeOffset.UtcNow,
                items,
                ex.Message);
        }
        finally
        {
            TryDeleteDirectory(jobTempDir, onLog);
        }
    }

    public static string ResolveCollisionSafePath(
        string desiredPath,
        bool overwriteExisting)
    {
        if (overwriteExisting || !File.Exists(desiredPath))
            return desiredPath;

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var counter = 1; counter < 10_000; counter++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({counter}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    private static void MoveOutputsToDestination(
        IReadOnlyList<JobFileItem> items,
        FetchForgeProfile profile,
        string outputDirectory,
        Action<string>? onLog)
    {
        foreach (var item in items)
        {
            if (item.Status == FileItemStatus.Failed)
                continue;

            var stageFile = profile.Ffmpeg.Enabled ? item.OutputPath : item.SourcePath;
            if (string.IsNullOrWhiteSpace(stageFile) || !File.Exists(stageFile))
            {
                item.Status = FileItemStatus.Failed;
                item.ErrorMessage ??= "Expected output file was not created.";
                continue;
            }

            var relative = string.IsNullOrWhiteSpace(item.RelativePath)
                ? Path.GetFileName(stageFile)
                : item.RelativePath;

            var relativeDir = Path.GetDirectoryName(relative) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(relative) + Path.GetExtension(stageFile);
            var destinationDir = string.IsNullOrEmpty(relativeDir)
                ? outputDirectory
                : Path.Combine(outputDirectory, relativeDir);
            Directory.CreateDirectory(destinationDir);

            var desiredPath = Path.Combine(destinationDir, fileName);
            var finalPath = ResolveCollisionSafePath(desiredPath, profile.OverwriteExisting);

            File.Move(stageFile, finalPath, overwrite: profile.OverwriteExisting);
            item.OutputPath = finalPath;
            item.Status = FileItemStatus.Completed;
            item.ProgressPercent = 100;
            item.StatusText = "Saved";

            onLog?.Invoke($"Saved: {finalPath}");
        }
    }

    private static void TryDeleteDirectory(string path, Action<string>? onLog)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onLog?.Invoke($"Warning: could not remove temporary folder '{path}': {ex.Message}");
        }
    }
}
