namespace FetchForge.Core.Models;

/// <summary>
/// Machine-specific application settings stored in
/// <c>%AppData%\FetchForge\settings.json</c>. Portable, shareable
/// configuration lives in <see cref="FetchForgeProfile"/>.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;

    public string OutputDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "Downloads");

    /// <summary>
    /// Root for per-job working folders. Each job uses its own
    /// subfolder, which is deleted when the job finishes.
    /// </summary>
    public string TemporaryDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "FetchForge");

    public string LastProfileName { get; set; } = FetchForgeProfile.DefaultName;

    public ToolPaths ToolPaths { get; set; } = new();

    /// <summary>Show advanced options in the option editors.</summary>
    public bool ShowAdvancedOptions { get; set; }
}

/// <summary>
/// Optional explicit executable locations. Blank values mean
/// "resolve from PATH".
/// </summary>
public sealed class ToolPaths
{
    public string? YtDlp { get; set; }
    public string? Ffmpeg { get; set; }
    public string? Ffprobe { get; set; }

    public string Resolve(string tool)
    {
        var configured = tool switch
        {
            ToolNames.YtDlp => YtDlp,
            ToolNames.Ffmpeg => Ffmpeg,
            ToolNames.Ffprobe => Ffprobe,
            _ => null
        };

        return string.IsNullOrWhiteSpace(configured)
            ? tool
            : Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
    }

    public ToolPaths Clone() => new()
    {
        YtDlp = YtDlp,
        Ffmpeg = Ffmpeg,
        Ffprobe = Ffprobe
    };
}
