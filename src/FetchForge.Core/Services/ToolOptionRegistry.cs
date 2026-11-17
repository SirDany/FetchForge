using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Registry of FetchForge's curated, typed tool options.
///
/// This registry does not inspect installed executables. Runtime support
/// is checked by <see cref="ToolOptionCapabilityMatcher"/>. Options not
/// curated here are reachable through the parsed help list and
/// extra arguments.
/// </summary>
public sealed class ToolOptionRegistry
{
    public const string FfmpegFilterChainDefault =
        "highpass=f=60,equalizer=f=150:t=q:w=1:g=3," +
        "equalizer=f=350:t=q:w=1:g=-1," +
        "equalizer=f=2800:t=q:w=1:g=2," +
        "treble=f=6500:g=1.5," +
        "loudnorm=I=-18:TP=-2:LRA=11";

    /// <summary>File extension used for each selectable encoder.</summary>
    public static readonly IReadOnlyDictionary<string, string> EncoderExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["libmp3lame"] = "mp3",
            ["aac"] = "m4a",
            ["libfdk_aac"] = "m4a",
            ["alac"] = "m4a",
            ["libopus"] = "opus",
            ["libvorbis"] = "ogg",
            ["flac"] = "flac",
            ["pcm_s16le"] = "wav",
            ["pcm_s24le"] = "wav"
        };

    private readonly List<ToolOptionDefinition> _ordered;
    private readonly Dictionary<string, ToolOptionDefinition> _definitions;

    public ToolOptionRegistry()
        : this(CreateDefaultDefinitions())
    {
    }

    /// <summary>
    /// Creates a registry from the supplied definitions.
    /// Useful for tests and future extension points.
    /// </summary>
    public ToolOptionRegistry(IEnumerable<ToolOptionDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _ordered = new List<ToolOptionDefinition>();
        _definitions = new Dictionary<string, ToolOptionDefinition>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.ValidateDefinition();

            if (!_definitions.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate tool option identifier: '{definition.Id}'.",
                    nameof(definitions));
            }

            _ordered.Add(definition);
        }
    }

    /// <summary>All registered definitions in registration order.</summary>
    public IReadOnlyList<ToolOptionDefinition> All => _ordered;

    public bool TryGet(string id, out ToolOptionDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            definition = null;
            return false;
        }

        return _definitions.TryGetValue(id, out definition);
    }

    public ToolOptionDefinition GetRequired(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _definitions.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Tool option '{id}' is not registered.");
    }

    /// <summary>
    /// Definitions belonging to <paramref name="tool"/>, in
    /// registration order (which groups categories logically).
    /// </summary>
    public IReadOnlyList<ToolOptionDefinition> GetByTool(string tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        return _ordered
            .Where(definition => string.Equals(
                definition.Tool, tool, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IEnumerable<ToolOptionDefinition> CreateDefaultDefinitions()
    {
        const string Y = ToolNames.YtDlp;
        const string F = ToolNames.Ffmpeg;

        // ---------------- yt-dlp: Download ----------------
        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.format",
            Tool = Y,
            DisplayName = "Format selection",
            Description =
                "Which formats to download. 'bestaudio/best' fetches the best " +
                "audio-only stream (or the best combined file if none). For " +
                "video use e.g. 'bv*+ba/b' and disable audio processing.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "bestaudio/best",
            Category = "Download",
            CommandLineArgument = "--format",
            Placeholder = "bestaudio/best"
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.yes-playlist",
            Tool = Y,
            DisplayName = "Download whole playlists",
            Description =
                "When a URL refers to both a video and a playlist, download " +
                "the playlist. When off, only the single video is downloaded.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = true,
            Category = "Download",
            CommandLineArgument = "--yes-playlist",
            FalseArguments = ["--no-playlist"]
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.playlist-items",
            Tool = Y,
            DisplayName = "Playlist items",
            Description =
                "Comma-separated playlist indices or ranges to download, " +
                "e.g. '1:5,8,10:'. Leave empty for all items.",
            ValueType = ToolOptionValueType.String,
            Category = "Download",
            CommandLineArgument = "--playlist-items",
            Placeholder = "1:5,8,10:"
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.output-template",
            Tool = Y,
            DisplayName = "Output filename template",
            Description =
                "yt-dlp output template, relative to the output folder. " +
                "Subfolders are allowed, e.g. '%(playlist)s/%(title)s.%(ext)s'.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "%(title)s.%(ext)s",
            Category = "Download",
            CommandLineArgument = "--output",
            Placeholder = "%(title)s.%(ext)s"
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.restrict-filenames",
            Tool = Y,
            DisplayName = "Restrict filenames",
            Description = "Use only ASCII characters and avoid spaces and '&' in filenames.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = false,
            Category = "Download",
            CommandLineArgument = "--restrict-filenames",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.download-archive",
            Tool = Y,
            DisplayName = "Download archive file",
            Description =
                "Records downloaded video IDs in this file and skips videos " +
                "already listed. Useful for keeping playlists in sync.",
            ValueType = ToolOptionValueType.String,
            Category = "Download",
            CommandLineArgument = "--download-archive",
            Placeholder = @"C:\Users\me\Music\archive.txt",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.ignore-config",
            Tool = Y,
            DisplayName = "Ignore yt-dlp config files",
            Description =
                "Do not load yt-dlp's own configuration files, so only " +
                "FetchForge's settings apply.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = false,
            Category = "Download",
            CommandLineArgument = "--ignore-config",
            IsAdvanced = true
        };

        // ---------------- yt-dlp: Audio extraction ----------------
        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.extract-audio",
            Tool = Y,
            DisplayName = "Extract audio (yt-dlp)",
            Description =
                "Convert downloads to audio-only files using yt-dlp. Only " +
                "applied when FetchForge audio processing is off; otherwise " +
                "FFmpeg performs the single conversion.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = true,
            Category = "Audio extraction",
            CommandLineArgument = "--extract-audio",
            AppliesWhen = OptionApplicability.WhenProcessingDisabled
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.audio-format",
            Tool = Y,
            DisplayName = "Audio format (yt-dlp)",
            Description =
                "Audio format used by yt-dlp's extraction. 'best' keeps the " +
                "original codec without re-encoding where possible.",
            ValueType = ToolOptionValueType.Enumeration,
            DefaultValue = "mp3",
            AllowedValues = ["best", "mp3", "m4a", "aac", "opus", "vorbis", "flac", "alac", "wav"],
            Category = "Audio extraction",
            CommandLineArgument = "--audio-format",
            AppliesWhen = OptionApplicability.WhenProcessingDisabled
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.audio-quality",
            Tool = Y,
            DisplayName = "Audio quality (yt-dlp)",
            Description =
                "0 (best) to 10 (worst) for VBR, or a bitrate such as '192K'.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "0",
            Category = "Audio extraction",
            CommandLineArgument = "--audio-quality",
            Placeholder = "0 or 192K",
            AppliesWhen = OptionApplicability.WhenProcessingDisabled
        };

        // ---------------- yt-dlp: Metadata ----------------
        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.embed-metadata",
            Tool = Y,
            DisplayName = "Embed metadata",
            Description =
                "Embed title, artist and other metadata. FFmpeg processing " +
                "carries embedded metadata over to the final file.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = true,
            Category = "Metadata",
            CommandLineArgument = "--embed-metadata",
            FalseArguments = ["--no-embed-metadata"]
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.embed-thumbnail",
            Tool = Y,
            DisplayName = "Embed thumbnail",
            Description =
                "Embed the thumbnail as cover art. Works best with audio " +
                "processing off (mp3/m4a/opus/flac outputs).",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = false,
            Category = "Metadata",
            CommandLineArgument = "--embed-thumbnail"
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.embed-chapters",
            Tool = Y,
            DisplayName = "Embed chapters",
            Description = "Add chapter markers to the file.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = false,
            Category = "Metadata",
            CommandLineArgument = "--embed-chapters",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.write-subs",
            Tool = Y,
            DisplayName = "Download subtitles",
            Description = "Write subtitle files next to the downloaded media.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = false,
            Category = "Metadata",
            CommandLineArgument = "--write-subs",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.sub-langs",
            Tool = Y,
            DisplayName = "Subtitle languages",
            Description = "Comma-separated languages or regexes, e.g. 'en.*,de'.",
            ValueType = ToolOptionValueType.String,
            Category = "Metadata",
            CommandLineArgument = "--sub-langs",
            Placeholder = "en.*,de",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.sponsorblock-remove",
            Tool = Y,
            DisplayName = "SponsorBlock: remove segments",
            Description =
                "Comma-separated SponsorBlock categories to cut out, e.g. " +
                "'sponsor,selfpromo,music_offtopic'.",
            ValueType = ToolOptionValueType.String,
            Category = "Metadata",
            CommandLineArgument = "--sponsorblock-remove",
            Placeholder = "sponsor,selfpromo",
            IsAdvanced = true
        };

        // ---------------- yt-dlp: Network ----------------
        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.limit-rate",
            Tool = Y,
            DisplayName = "Rate limit",
            Description = "Maximum download rate in bytes per second, e.g. '2M' or '500K'.",
            ValueType = ToolOptionValueType.String,
            Category = "Network",
            CommandLineArgument = "--limit-rate",
            Placeholder = "2M"
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.retries",
            Tool = Y,
            DisplayName = "Retries",
            Description = "Number of retries for failed downloads (yt-dlp default: 10).",
            ValueType = ToolOptionValueType.Integer,
            Minimum = 0,
            Maximum = 100,
            Category = "Network",
            CommandLineArgument = "--retries",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.concurrent-fragments",
            Tool = Y,
            DisplayName = "Concurrent fragments",
            Description =
                "Number of fragments of a DASH/HLS download fetched in parallel.",
            ValueType = ToolOptionValueType.Integer,
            Minimum = 1,
            Maximum = 32,
            Category = "Network",
            CommandLineArgument = "--concurrent-fragments",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.sleep-interval",
            Tool = Y,
            DisplayName = "Sleep between downloads (s)",
            Description = "Seconds to wait before each download. Helps avoid rate limiting.",
            ValueType = ToolOptionValueType.Decimal,
            Minimum = 0,
            Maximum = 3600,
            Category = "Network",
            CommandLineArgument = "--sleep-interval",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.proxy",
            Tool = Y,
            DisplayName = "Proxy",
            Description = "HTTP/HTTPS/SOCKS proxy URL, e.g. 'socks5://127.0.0.1:1080'.",
            ValueType = ToolOptionValueType.String,
            Category = "Network",
            CommandLineArgument = "--proxy",
            Placeholder = "socks5://127.0.0.1:1080",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.cookies-from-browser",
            Tool = Y,
            DisplayName = "Cookies from browser",
            Description =
                "Load cookies from a browser for sites that need a login, " +
                "e.g. 'firefox', 'chrome', 'edge' or 'firefox:ProfileName'.",
            ValueType = ToolOptionValueType.String,
            Category = "Network",
            CommandLineArgument = "--cookies-from-browser",
            Placeholder = "firefox",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.cookies",
            Tool = Y,
            DisplayName = "Cookies file",
            Description = "Netscape-format cookies file to read cookies from.",
            ValueType = ToolOptionValueType.String,
            Category = "Network",
            CommandLineArgument = "--cookies",
            Placeholder = @"C:\path\cookies.txt",
            IsAdvanced = true
        };

        // ---------------- FFmpeg: Encoding ----------------
        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.audio-codec",
            Tool = F,
            DisplayName = "Audio codec",
            Description = "Encoder used for the processed output file.",
            ValueType = ToolOptionValueType.Enumeration,
            DefaultValue = "libmp3lame",
            AllowedValues = ["libmp3lame", "aac", "libopus", "libvorbis", "flac", "alac", "pcm_s16le"],
            Category = "Encoding",
            CommandLineArgument = "-c:a",
            ValueIsFfmpegEncoder = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.audio-quality",
            Tool = F,
            DisplayName = "VBR quality",
            Description =
                "Codec-specific variable-bitrate quality. libmp3lame: 0 (best) " +
                "to 9; libvorbis: -1 to 10 (higher is better). Ignored by " +
                "lossless codecs and libopus. Leave empty when using a bitrate.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "2",
            Category = "Encoding",
            CommandLineArgument = "-q:a",
            Placeholder = "2"
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.audio-bitrate",
            Tool = F,
            DisplayName = "Bitrate",
            Description =
                "Constant/target bitrate, e.g. '192k'. Use this for libopus " +
                "and aac; clear 'VBR quality' when setting it.",
            ValueType = ToolOptionValueType.String,
            Category = "Encoding",
            CommandLineArgument = "-b:a",
            Placeholder = "192k"
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.output-extension",
            Tool = F,
            DisplayName = "Output file type",
            Description =
                "File extension of the processed file. 'auto' picks the " +
                "usual container for the selected codec.",
            ValueType = ToolOptionValueType.Enumeration,
            DefaultValue = "auto",
            AllowedValues = ["auto", "mp3", "m4a", "opus", "ogg", "flac", "wav", "mka"],
            Category = "Encoding"
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.sample-rate",
            Tool = F,
            DisplayName = "Sample rate (Hz)",
            Description = "Resample the output, e.g. 44100 or 48000. Empty keeps the source rate.",
            ValueType = ToolOptionValueType.Integer,
            Minimum = 8000,
            Maximum = 192000,
            Category = "Encoding",
            CommandLineArgument = "-ar",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.channels",
            Tool = F,
            DisplayName = "Channels",
            Description = "Number of output channels (1 = mono, 2 = stereo). Empty keeps the source.",
            ValueType = ToolOptionValueType.Integer,
            Minimum = 1,
            Maximum = 8,
            Category = "Encoding",
            CommandLineArgument = "-ac",
            IsAdvanced = true
        };

        // ---------------- FFmpeg: Filters ----------------
        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.filter-chain",
            Tool = F,
            DisplayName = "Audio filter chain",
            Description =
                "FFmpeg audio filtergraph applied before encoding. The " +
                "default applies a gentle EQ and EBU R128 loudness " +
                "normalisation. Leave empty for no filtering.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = FfmpegFilterChainDefault,
            Category = "Filters",
            CommandLineArgument = "-af",
            Placeholder = "loudnorm=I=-16:TP=-1.5:LRA=11"
        };

        // ---------------- FFmpeg: Streams & metadata ----------------
        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.copy-metadata",
            Tool = F,
            DisplayName = "Copy metadata",
            Description = "Copy global metadata (title, artist, ...) from the downloaded file.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = true,
            Category = "Streams & metadata",
            CommandLineArgument = "-map_metadata",
            TrueArguments = ["-map_metadata", "0"],
            FalseArguments = ["-map_metadata", "-1"]
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.strip-video",
            Tool = F,
            DisplayName = "Drop video streams",
            Description =
                "Discard video and embedded cover-art streams so the output " +
                "is audio only.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = true,
            Category = "Streams & metadata",
            CommandLineArgument = "-vn"
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.threads",
            Tool = F,
            DisplayName = "Threads per job",
            Description =
                "Encoder threads per FFmpeg process. Empty lets FFmpeg decide. " +
                "With many concurrent jobs, 1 is usually most efficient.",
            ValueType = ToolOptionValueType.Integer,
            Minimum = 0,
            Maximum = 64,
            Category = "Streams & metadata",
            CommandLineArgument = "-threads",
            IsAdvanced = true
        };
    }
}
