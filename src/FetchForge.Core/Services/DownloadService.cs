using System.Collections.Concurrent;
using System.Globalization;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class DownloadService
{
    private const string ProgressPrefix = "ff-progress:";
    private const string FilePrefix = "ff-file:";

    private readonly IProcessRunner _processRunner;
    private readonly CommandBuilder _commandBuilder;

    public DownloadService(
        IProcessRunner processRunner,
        CommandBuilder commandBuilder)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
        _commandBuilder = commandBuilder
            ?? throw new ArgumentNullException(nameof(commandBuilder));
    }

    public static bool TryParseProgressLine(
        string line,
        out string id,
        out double? percent,
        out string speed,
        out string eta,
        out string title)
    {
        id = string.Empty;
        percent = null;
        speed = string.Empty;
        eta = string.Empty;
        title = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            return false;

        var payload = trimmed[ProgressPrefix.Length..];
        var parts = payload.Split('|', 5);
        if (parts.Length < 2)
            return false;

        id = parts[0].Trim();
        if (id.Length == 0)
            id = "download";

        var pctText = parts[1].Trim().TrimEnd('%').Trim();
        if (double.TryParse(pctText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedPct))
            percent = Math.Clamp(parsedPct, 0, 100);

        if (parts.Length > 2)
            speed = NormalizePlaceholder(parts[2]);

        if (parts.Length > 3)
            eta = NormalizePlaceholder(parts[3]);

        if (parts.Length > 4)
            title = NormalizePlaceholder(parts[4]);

        return true;
    }

    public static bool TryParseFileLine(string line, out string filePath)
    {
        filePath = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(FilePrefix, StringComparison.Ordinal))
            return false;

        filePath = trimmed[FilePrefix.Length..].Trim();
        return filePath.Length > 0;
    }

    public async Task<IReadOnlyList<JobFileItem>> DownloadAsync(
        FetchForgeProfile profile,
        string url,
        string workingDirectory,
        ToolPaths toolPaths,
        IProgress<JobProgressUpdate>? progress = null,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default,
        IEnumerable<ToolCapabilityResult>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(toolPaths);

        Directory.CreateDirectory(workingDirectory);

        var command = _commandBuilder.BuildYtDlpCommand(
            profile,
            url,
            workingDirectory,
            toolPaths,
            capabilities,
            forExecution: true);

        if (command.HasErrors)
        {
            var errors = string.Join("; ", command.Issues
                .Where(i => i.Severity == OptionValidationSeverity.Error)
                .Select(i => i.Message));
            throw new InvalidOperationException($"Invalid yt-dlp configuration: {errors}");
        }

        onLog?.Invoke($"Running: {command.FormattedCommandLine}");
        progress?.Report(new JobProgressUpdate(
            JobStage.Downloading,
            "Starting yt-dlp download...",
            0));

        var trackedItems = new Dictionary<string, JobFileItem>(StringComparer.Ordinal);
        var downloadedFiles = new List<string>();
        var errorTail = new ConcurrentQueue<string>();
        var sync = new object();

        void HandleLine(string line, bool isError)
        {
            if (TryParseProgressLine(
                    line,
                    out var id,
                    out var pct,
                    out var speed,
                    out var eta,
                    out var title))
            {
                JobFileItem item;
                double overall;

                lock (sync)
                {
                    if (!trackedItems.TryGetValue(id, out item!))
                    {
                        item = new JobFileItem
                        {
                            Id = id,
                            Title = string.IsNullOrWhiteSpace(title) ? id : title,
                            Status = FileItemStatus.Downloading
                        };
                        trackedItems[id] = item;
                    }

                    if (!string.IsNullOrWhiteSpace(title))
                        item.Title = title;

                    if (pct.HasValue)
                        item.ProgressPercent = pct.Value;

                    item.Status = FileItemStatus.Downloading;
                    item.StatusText = BuildProgressText(pct, speed, eta);
                    overall = trackedItems.Values.Average(i => i.ProgressPercent);
                }

                progress?.Report(new JobProgressUpdate(
                    JobStage.Downloading,
                    $"Downloading {item.Title} ({item.StatusText})",
                    profile.Ffmpeg.Enabled ? overall * 0.5 : overall,
                    item));
                return;
            }

            if (TryParseFileLine(line, out var rawFilePath))
            {
                var fullPath = Path.IsPathRooted(rawFilePath)
                    ? Path.GetFullPath(rawFilePath)
                    : Path.GetFullPath(Path.Combine(workingDirectory, rawFilePath));

                lock (sync)
                {
                    if (!downloadedFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        downloadedFiles.Add(fullPath);

                    var match = trackedItems.Values.FirstOrDefault(i =>
                        string.IsNullOrEmpty(i.SourcePath) &&
                        fullPath.Contains(i.Id, StringComparison.OrdinalIgnoreCase))
                        ?? trackedItems.Values.LastOrDefault(i => string.IsNullOrEmpty(i.SourcePath));

                    if (match is not null)
                    {
                        match.SourcePath = fullPath;
                        match.RelativePath = Path.GetRelativePath(workingDirectory, fullPath);
                        match.Status = FileItemStatus.Downloaded;
                        match.ProgressPercent = 100;
                        match.StatusText = "Downloaded";
                    }
                }

                onLog?.Invoke($"Downloaded: {Path.GetFileName(fullPath)}");
                return;
            }

            if (isError)
            {
                errorTail.Enqueue(line);
                while (errorTail.Count > 15)
                    errorTail.TryDequeue(out _);
            }

            onLog?.Invoke(line);
        }

        var exitCode = await _processRunner.RunAsync(
            command.Executable,
            command.Arguments,
            workingDirectory,
            line => HandleLine(line, isError: false),
            line => HandleLine(line, isError: true),
            cancellationToken);

        if (exitCode != 0)
        {
            var detail = errorTail.IsEmpty
                ? $"yt-dlp exited with code {exitCode}."
                : $"yt-dlp exited with code {exitCode}: {string.Join(" | ", errorTail.TakeLast(3))}";
            throw new InvalidOperationException(detail);
        }

        var finalPaths = downloadedFiles
            .Where(File.Exists)
            .ToList();

        if (finalPaths.Count == 0 && Directory.Exists(workingDirectory))
        {
            finalPaths = Directory.GetFiles(workingDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !IsTemporaryDownloadArtifact(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var resultItems = new List<JobFileItem>(finalPaths.Count);
        for (var i = 0; i < finalPaths.Count; i++)
        {
            var path = finalPaths[i];
            var existing = trackedItems.Values.FirstOrDefault(item =>
                string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));

            var item = existing ?? new JobFileItem
            {
                Id = $"file-{i + 1}",
                Title = Path.GetFileNameWithoutExtension(path),
                SourcePath = path
            };

            item.SourcePath = path;
            item.RelativePath = Path.GetRelativePath(workingDirectory, path);
            if (string.IsNullOrWhiteSpace(item.Title))
                item.Title = Path.GetFileNameWithoutExtension(path);
            item.Status = FileItemStatus.Downloaded;
            item.ProgressPercent = 100;
            item.StatusText = "Downloaded";

            resultItems.Add(item);
        }

        return resultItems;
    }

    private static string NormalizePlaceholder(string value)
    {
        var trimmed = value.Trim();
        return trimmed is "NA" or "Unknown" ? string.Empty : trimmed;
    }

    private static string BuildProgressText(double? pct, string speed, string eta)
    {
        var parts = new List<string>();
        if (pct.HasValue)
            parts.Add($"{pct.Value:0.0}%");
        if (!string.IsNullOrWhiteSpace(speed))
            parts.Add(speed);
        if (!string.IsNullOrWhiteSpace(eta))
            parts.Add($"ETA {eta}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Downloading...";
    }

    private static bool IsTemporaryDownloadArtifact(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ytdl", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}
