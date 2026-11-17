using System.Globalization;

namespace FetchForge.Core.Models;

/// <summary>Well-known tool names.</summary>
public static class ToolNames
{
    public const string YtDlp = "yt-dlp";
    public const string Ffmpeg = "ffmpeg";
    public const string Ffprobe = "ffprobe";
}

/// <summary>
/// When an option is applied relative to FFmpeg post-processing.
/// </summary>
public enum OptionApplicability
{
    Always,

    /// <summary>
    /// Only applied when FetchForge's FFmpeg stage is disabled, e.g.
    /// yt-dlp's own audio extraction (prevents double lossy encoding).
    /// </summary>
    WhenProcessingDisabled
}

/// <summary>
/// Converts option values between CLR values and the invariant string
/// representation stored in profiles.
/// </summary>
public static class OptionValueFormatter
{
    public static string? Format(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static bool TryParseBoolean(string? text, out bool value)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes" or "on":
                value = true;
                return true;
            case "false" or "0" or "no" or "off":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    public static bool TryParseDecimal(string? text, out double value) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    public static bool TryParseInteger(string? text, out long value) =>
        long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    /// <summary>Splits a newline-separated string list.</summary>
    public static IReadOnlyList<string> SplitList(string? text) =>
        (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
}
