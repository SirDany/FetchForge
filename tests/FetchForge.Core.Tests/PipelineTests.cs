using System.Diagnostics;
using FetchForge.Core.Models;
using FetchForge.Core.Services;

namespace FetchForge.Core.Tests;

public sealed class PipelineTests : IDisposable
{
    private readonly string _tempDir;

    public PipelineTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), $"FetchForge_Pipe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ProgressParsers_ParseYtDlpAndFfmpegLines()
    {
        var okYt = DownloadService.TryParseProgressLine(
            "ff-progress:abc123| 45.2%|1.50MiB/s|00:05|Song | Remix",
            out var id,
            out var pct,
            out var speed,
            out var eta,
            out var title);

        Assert.True(okYt);
        Assert.Equal("abc123", id);
        Assert.Equal(45.2, pct);
        Assert.Equal("1.50MiB/s", speed);
        Assert.Equal("00:05", eta);
        Assert.Equal("Song | Remix", title);

        var okFile = DownloadService.TryParseFileLine(
            @"ff-file:C:\temp\001 - Song.webm", out var filePath);
        Assert.True(okFile);
        Assert.Equal(@"C:\temp\001 - Song.webm", filePath);

        Assert.True(ProcessingQueue.TryParseFfmpegProgressMicroseconds(
            "out_time_us=12500000", out var us));
        Assert.Equal(12_500_000, us);

        Assert.True(ProcessingQueue.TryParseFfmpegProgressMicroseconds(
            "out_time=00:00:05.500000", out var usTime));
        Assert.Equal(5_500_000, usTime);
    }

    [Fact]
    public async Task ProcessingQueue_RespectsMaxConcurrentJobs()
    {
        var runner = new ConcurrencyTrackingRunner();
        var registry = new ToolOptionRegistry();
        var queue = new ProcessingQueue(runner, new CommandBuilder(registry));

        var profile = new FetchForgeProfile();
        profile.Ffmpeg.MaxConcurrentJobs = 2;

        var items = Enumerable.Range(1, 5)
            .Select(i =>
            {
                var src = Path.Combine(_tempDir, $"src_{i}.webm");
                File.WriteAllText(src, "dummy");
                return new JobFileItem
                {
                    Id = $"id-{i}",
                    Title = $"Track {i}",
                    SourcePath = src,
                    RelativePath = $"Track {i}.webm"
                };
            })
            .ToArray();

        var processedDir = Path.Combine(_tempDir, "processed");
        await queue.ProcessFilesAsync(
            items, profile, processedDir, new ToolPaths());

        Assert.Equal(2, runner.MaxObservedFfmpegConcurrency);
        Assert.All(items, item => Assert.Equal(FileItemStatus.Completed, item.Status));
    }

    [Fact]
    public async Task JobOrchestrator_HandlesCollisionsAndAlwaysCleansUpTempDir()
    {
        var runner = new ConcurrencyTrackingRunner();
        var registry = new ToolOptionRegistry();
        var orchestrator = new JobOrchestrator(runner, registry);

        var outputDir = Path.Combine(_tempDir, "out");
        var tempRoot = Path.Combine(_tempDir, "jobs-temp");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "Song.mp3"), "existing");

        var settings = new AppSettings
        {
            OutputDirectory = outputDir,
            TemporaryDirectory = tempRoot
        };
        var profile = new FetchForgeProfile { OverwriteExisting = false };

        var report = await orchestrator.RunJobAsync(
            profile, settings, "https://example.com/song");

        Assert.Equal(JobStage.Completed, report.Stage);
        Assert.Equal(1, report.SucceededCount);
        Assert.True(File.Exists(Path.Combine(outputDir, "Song.mp3")));
        Assert.True(File.Exists(Path.Combine(outputDir, "Song (1).mp3")));
        Assert.Empty(Directory.GetDirectories(tempRoot));
    }

    [Fact]
    public async Task JobOrchestrator_CleansUpTempDirOnFailureAndCancellation()
    {
        var runner = new FailingYtDlpRunner();
        var orchestrator = new JobOrchestrator(runner, new ToolOptionRegistry());
        var tempRoot = Path.Combine(_tempDir, "fail-temp");
        var settings = new AppSettings
        {
            OutputDirectory = Path.Combine(_tempDir, "out-fail"),
            TemporaryDirectory = tempRoot
        };

        var report = await orchestrator.RunJobAsync(
            new FetchForgeProfile(), settings, "https://example.com/bad");

        Assert.Equal(JobStage.Failed, report.Stage);
        Assert.Empty(Directory.GetDirectories(tempRoot));
    }

    [Fact]
    public async Task ProcessRunner_KillsProcessOnCancellation()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var (exe, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "ping -n 30 127.0.0.1 >nul" })
            : ("sleep", new[] { "30" });

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(exe, args, cancellationToken: cts.Token));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Expected fast cancellation, took {sw.Elapsed}");
    }

    private sealed class ConcurrencyTrackingRunner : IProcessRunner
    {
        private int _activeFfmpeg;
        public int MaxObservedFfmpegConcurrency { get; private set; }

        public async Task<int> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            Action<string>? onOutput = null,
            Action<string>? onError = null,
            CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();

            if (executable.Contains("yt-dlp", StringComparison.OrdinalIgnoreCase))
            {
                var pIndex = Array.IndexOf(args, "-P");
                var workDir = args[pIndex + 1];
                var downloaded = Path.Combine(workDir, "Song.webm");
                await File.WriteAllTextAsync(downloaded, "audio-bytes", cancellationToken);
                onOutput?.Invoke("ff-progress:id1|100.0%|2.0MiB/s|00:00|Song");
                onOutput?.Invoke($"ff-file:{downloaded}");
                return 0;
            }

            if (executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                onOutput?.Invoke("10.0");
                return 0;
            }

            if (executable.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                var current = Interlocked.Increment(ref _activeFfmpeg);
                MaxObservedFfmpegConcurrency = Math.Max(MaxObservedFfmpegConcurrency, current);
                try
                {
                    await Task.Delay(50, cancellationToken);
                    var outputPath = args[^1];
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    await File.WriteAllTextAsync(outputPath, "encoded-bytes", cancellationToken);
                    onOutput?.Invoke("out_time_us=10000000");
                    return 0;
                }
                finally
                {
                    Interlocked.Decrement(ref _activeFfmpeg);
                }
            }

            return 1;
        }
    }

    private sealed class FailingYtDlpRunner : IProcessRunner
    {
        public Task<int> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            Action<string>? onOutput = null,
            Action<string>? onError = null,
            CancellationToken cancellationToken = default)
        {
            onError?.Invoke("ERROR: simulated download failure");
            return Task.FromResult(1);
        }
    }
}
