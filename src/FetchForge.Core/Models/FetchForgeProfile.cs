using System.Text.Json.Serialization;

namespace FetchForge.Core.Models;

/// <summary>
/// A named, portable configuration for both tools (schema v2).
/// Option values are stored as invariant strings keyed by the stable
/// option ID; options without an entry use their registry default.
/// </summary>
public sealed class FetchForgeProfile
{
    public const int CurrentSchemaVersion = 2;
    public const string DefaultName = "Default";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = DefaultName;

    /// <summary>
    /// Prefix playlist downloads with their index ("001 - Title") so
    /// files sort in playlist order.
    /// </summary>
    public bool PreservePlaylistOrder { get; set; } = true;

    /// <summary>
    /// Replace files that already exist in the output folder. When
    /// false, a numbered suffix is added instead.
    /// </summary>
    public bool OverwriteExisting { get; set; }

    public ToolProfileSettings YtDlp { get; set; } = new();

    public FfmpegProfileSettings Ffmpeg { get; set; } = new();

    public ToolProfileSettings GetToolSettings(string tool) => tool switch
    {
        ToolNames.YtDlp => YtDlp,
        ToolNames.Ffmpeg => Ffmpeg,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
    };

    /// <summary>
    /// Effective value of an option: the profile's override, or the
    /// registry default. Null means "not set / not emitted".
    /// </summary>
    public string? GetValue(ToolOptionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return GetToolSettings(definition.Tool).Options
            .TryGetValue(definition.Id, out var value)
            ? value
            : OptionValueFormatter.Format(definition.DefaultValue);
    }

    public bool IsOverridden(ToolOptionDefinition definition) =>
        GetToolSettings(definition.Tool).Options.ContainsKey(definition.Id);

    /// <summary>
    /// Stores a value. Setting the registry default removes the
    /// override so future default changes apply.
    /// </summary>
    public void SetValue(ToolOptionDefinition definition, string? value)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var options = GetToolSettings(definition.Tool).Options;
        var defaultValue = OptionValueFormatter.Format(definition.DefaultValue);

        if (string.Equals(value ?? string.Empty, defaultValue ?? string.Empty,
                StringComparison.Ordinal))
        {
            options.Remove(definition.Id);
        }
        else
        {
            options[definition.Id] = value ?? string.Empty;
        }
    }

    public void ResetValue(ToolOptionDefinition definition) =>
        GetToolSettings(definition.Tool).Options.Remove(definition.Id);

    /// <summary>
    /// Makes dictionaries case-insensitive and fills missing sections
    /// after deserialization.
    /// </summary>
    public FetchForgeProfile Normalize()
    {
        YtDlp ??= new ToolProfileSettings();
        Ffmpeg ??= new FfmpegProfileSettings();
        YtDlp.Normalize();
        Ffmpeg.Normalize();
        Ffmpeg.MaxConcurrentJobs = Math.Clamp(
            Ffmpeg.MaxConcurrentJobs, 1, FfmpegProfileSettings.MaxAllowedConcurrentJobs);
        Name = string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();
        SchemaVersion = CurrentSchemaVersion;
        return this;
    }

    public FetchForgeProfile Clone(string? newName = null) => new()
    {
        SchemaVersion = SchemaVersion,
        Name = newName ?? Name,
        PreservePlaylistOrder = PreservePlaylistOrder,
        OverwriteExisting = OverwriteExisting,
        YtDlp = YtDlp.CloneBase(),
        Ffmpeg = Ffmpeg.CloneFfmpeg()
    };
}

public class ToolProfileSettings
{
    /// <summary>Option ID → invariant string value.</summary>
    public Dictionary<string, string> Options { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Additional raw arguments, one per entry, appended after the
    /// curated options. Never passed through a shell.
    /// </summary>
    public List<string> ExtraArguments { get; set; } = new();

    internal void Normalize()
    {
        Options = new Dictionary<string, string>(
            Options ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        ExtraArguments = (ExtraArguments ?? new List<string>())
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .ToList();
    }

    internal ToolProfileSettings CloneBase() => new()
    {
        Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase),
        ExtraArguments = new List<string>(ExtraArguments)
    };
}

public sealed class FfmpegProfileSettings : ToolProfileSettings
{
    public const int DefaultConcurrentJobs = 16;
    public const int MaxAllowedConcurrentJobs = 64;

    /// <summary>Whether the FFmpeg post-processing stage runs.</summary>
    public bool Enabled { get; set; } = true;

    public int MaxConcurrentJobs { get; set; } = DefaultConcurrentJobs;

    internal FfmpegProfileSettings CloneFfmpeg() => new()
    {
        Enabled = Enabled,
        MaxConcurrentJobs = MaxConcurrentJobs,
        Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase),
        ExtraArguments = new List<string>(ExtraArguments)
    };
}
