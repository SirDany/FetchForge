using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Registry of FetchForge-owned tool option metadata.
///
/// This registry does not inspect installed executables. Runtime
/// capability discovery should be implemented separately and matched
/// against these stable option identifiers.
/// </summary>
public sealed class ToolOptionRegistry
{
    private readonly IReadOnlyDictionary<string, ToolOptionDefinition>
        _definitions;

    public ToolOptionRegistry()
        : this(CreateDefaultDefinitions())
    {
    }

    /// <summary>
    /// Creates a registry from the supplied definitions.
    /// Useful for tests and future extension points.
    /// </summary>
    public ToolOptionRegistry(
        IEnumerable<ToolOptionDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var registry = new Dictionary<string, ToolOptionDefinition>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.ValidateDefinition();

            if (!registry.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate tool option identifier: '{definition.Id}'.",
                    nameof(definitions));
            }
        }

        _definitions = registry;
    }

    /// <summary>
    /// All registered definitions.
    /// </summary>
    public IReadOnlyCollection<ToolOptionDefinition> All =>
        _definitions.Values.ToArray();

    /// <summary>
    /// Finds a definition by its stable FetchForge identifier.
    /// </summary>
    public bool TryGet(
        string id,
        out ToolOptionDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            definition = null;
            return false;
        }

        return _definitions.TryGetValue(id, out definition);
    }

    /// <summary>
    /// Gets a definition by identifier or throws if it is not registered.
    /// </summary>
    public ToolOptionDefinition GetRequired(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _definitions.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Tool option '{id}' is not registered.");
    }

    /// <summary>
    /// Returns all definitions belonging to the specified tool.
    /// </summary>
    public IReadOnlyList<ToolOptionDefinition> GetByTool(string tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        return _definitions.Values
            .Where(definition =>
                string.Equals(
                    definition.Tool,
                    tool,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(definition => definition.Category)
            .ThenBy(definition => definition.DisplayName)
            .ToArray();
    }

    private static IEnumerable<ToolOptionDefinition>
        CreateDefaultDefinitions()
    {
        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.format",
            Tool = "yt-dlp",
            DisplayName = "Format selection",
            Description =
                "Selects the video and audio formats to download.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "bestvideo*+bestaudio/best",
            Category = "Download",
            CommandLineArgument = "--format"
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.output-template",
            Tool = "yt-dlp",
            DisplayName = "Output filename template",
            Description =
                "Defines how downloaded files are named and organized.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "%(title)s.%(ext)s",
            Category = "Download",
            CommandLineArgument = "--output",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "yt-dlp.embed-metadata",
            Tool = "yt-dlp",
            DisplayName = "Embed metadata",
            Description =
                "Embeds available metadata into downloaded media files.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = true,
            Category = "Metadata",
            CommandLineArgument = "--embed-metadata"
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.audio-codec",
            Tool = "ffmpeg",
            DisplayName = "Audio codec",
            Description =
                "Selects the codec used to encode the output audio.",
            ValueType = ToolOptionValueType.Enumeration,
            DefaultValue = "libmp3lame",
            Category = "Audio",
            CommandLineArgument = "-c:a",
            AllowedValues =
            [
                "libmp3lame",
                "aac",
                "libopus",
                "flac",
                "pcm_s16le"
            ],
            IsAdvanced = false
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.audio-quality",
            Tool = "ffmpeg",
            DisplayName = "Audio quality",
            Description =
                "Sets the codec-specific audio quality value. Its meaning " +
                "depends on the selected encoder.",
            ValueType = ToolOptionValueType.String,
            DefaultValue = "2",
            Category = "Audio",
            CommandLineArgument = "-q:a",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.filter-chain",
            Tool = "ffmpeg",
            DisplayName = "Audio filter chain",
            Description =
                "Applies audio filters before encoding the output.",
            ValueType = ToolOptionValueType.String,
            DefaultValue =
                "highpass=f=60,equalizer=f=150:t=q:w=1:g=3," +
                "equalizer=f=350:t=q:w=1:g=-1," +
                "equalizer=f=2800:t=q:w=1:g=2," +
                "treble=f=6500:g=1.5," +
                "loudnorm=I=-18:TP=-2:LRA=11",
            Category = "Audio",
            CommandLineArgument = "-af",
            IsAdvanced = true
        };

        yield return new ToolOptionDefinition
        {
            Id = "ffmpeg.overwrite-output",
            Tool = "ffmpeg",
            DisplayName = "Overwrite output",
            Description =
                "Allows FFmpeg to overwrite an existing output file.",
            ValueType = ToolOptionValueType.Boolean,
            DefaultValue = false,
            Category = "Output",
            CommandLineArgument = "-y",
            IsAdvanced = true
        };
    }
}


