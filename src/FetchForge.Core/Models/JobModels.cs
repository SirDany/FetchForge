using System.Text;

namespace FetchForge.Core.Models;

public enum JobStage
{
    Idle,
    Downloading,
    Processing,
    Completed,
    Cancelled,
    Failed
}

public enum FileItemStatus
{
    Queued,
    Downloading,
    Downloaded,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public sealed class JobFileItem
{
    public required string Id { get; init; }
    public string Title { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public string? OutputPath { get; set; }
    public FileItemStatus Status { get; set; } = FileItemStatus.Queued;
    public double ProgressPercent { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
    public TimeSpan ProcessingElapsed { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed record JobProgressUpdate(
    JobStage Stage,
    string Message,
    double OverallPercent,
    JobFileItem? Item = null);

public sealed record JobReport(
    JobStage Stage,
    string Url,
    string OutputDirectory,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<JobFileItem> Items,
    string? ErrorMessage = null)
{
    public int TotalCount => Items.Count;
    public int SucceededCount => Items.Count(i => i.Status == FileItemStatus.Completed);
    public int FailedCount => Items.Count(i => i.Status == FileItemStatus.Failed);
    public int CancelledCount => Items.Count(i => i.Status == FileItemStatus.Cancelled);
    public TimeSpan Duration => CompletedAt - StartedAt;

    public string FormatReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== FetchForge Job Report ({Stage}) ===");
        sb.AppendLine($"URL: {Url}");
        sb.AppendLine($"Output directory: {OutputDirectory}");
        sb.AppendLine($"Elapsed: {Duration:mm\\:ss\\.f}");
        sb.AppendLine(
            $"Files: {SucceededCount} succeeded, {FailedCount} failed, {CancelledCount} cancelled (total {TotalCount})");

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
            sb.AppendLine($"Error: {ErrorMessage}");

        if (Items.Count > 0)
        {
            sb.AppendLine();
            foreach (var item in Items)
            {
                var name = string.IsNullOrWhiteSpace(item.Title)
                    ? Path.GetFileName(item.OutputPath ?? item.SourcePath)
                    : item.Title;
                var duration = item.Duration.HasValue
                    ? $" ({item.Duration.Value:mm\\:ss})"
                    : string.Empty;
                var target = item.OutputPath ?? item.SourcePath;
                var error = string.IsNullOrWhiteSpace(item.ErrorMessage)
                    ? string.Empty
                    : $" — {item.ErrorMessage}";

                sb.AppendLine(
                    $"[{item.Status}] {name}{duration} -> {target}{error}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
