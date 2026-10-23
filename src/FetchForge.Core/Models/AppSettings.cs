
namespace FetchForge.Core.Models;

public sealed class AppSettings
{
    public string OutputDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "Downloads");

    public int MaxConcurrentFfmpegJobs { get; set; } = 16;

    public bool PreservePlaylistOrder { get; set; } = true;
    public bool AudioEnhancementEnabled { get; set; } = true;

    public string TemporaryDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "FetchForge");
}
