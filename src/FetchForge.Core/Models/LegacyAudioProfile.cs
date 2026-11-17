namespace FetchForge.Core.Models;

/// <summary>
/// Schema-less profile formats written by early FetchForge builds.
/// Only used to migrate old files to <see cref="FetchForgeProfile"/>.
/// Covers both the original <c>AudioProfile</c> class and the
/// sample <c>profiles/Default.json</c> shape.
/// </summary>
internal sealed class LegacyAudioProfile
{
    public string? Name { get; set; }

    // AudioProfile (v1 class)
    public bool? Enabled { get; set; }
    public string? FilterChain { get; set; }
    public string? OutputCodec { get; set; }
    public string? OutputQuality { get; set; }

    // Default.json (v1 sample)
    public bool? EnhancementEnabled { get; set; }
    public int? MaxParallelFfmpegJobs { get; set; }
    public string? FfmpegFilter { get; set; }
    public string? YtDlpArguments { get; set; }
    public string? FfmpegArguments { get; set; }
}
