using System.Text.RegularExpressions;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Parses option listings from <c>yt-dlp --help</c>,
/// <c>ffmpeg -h long</c> and <c>ffmpeg -encoders</c>.
/// </summary>
public static partial class ToolHelpParser
{
    // "  Network Options:"
    [GeneratedRegex(@"^ {2}(?<name>\S[^:]*):\s*$")]
    private static partial Regex YtDlpSectionPattern();

    // "    -f, --format FORMAT        Video format code..."
    [GeneratedRegex(@"^ {4}(?<opt>-\S.*?)(?: {2,}(?<desc>\S.*))?$")]
    private static partial Regex YtDlpOptionPattern();

    [GeneratedRegex(@"\(Alias(?:es)?: (?<aliases>[^)]+)\)")]
    private static partial Regex YtDlpAliasPattern();

    // "-ar[:<stream_spec>] <rate>  set audio sampling rate (in Hz)"
    [GeneratedRegex(
        @"^(?<sw>-[^\s\[<]+)(?<spec>\[:<[^>]+>\])?" +
        @"(?: <(?<ph>[^>]*)>)?(?: {2,}(?<desc>\S.*))?\s*$")]
    private static partial Regex FfmpegOptionPattern();

    // "  -threads           <int>        ED.VA...... set the number of threads..."
    [GeneratedRegex(
        @"^  (?<sw>-[^\s]+)\s+<(?<ph>[^>]*)>\s+[A-Z.]{11}\s+(?<desc>.*)$")]
    private static partial Regex FfmpegAvOptionPattern();

    // " A....D aac                  AAC (Advanced Audio Coding)"
    [GeneratedRegex(
        @"^ (?<type>[VASD])[.A-Z]{5} (?<name>\S+)\s+(?<desc>.*)$")]
    private static partial Regex FfmpegEncoderPattern();

    public static IReadOnlyList<HelpOptionInfo> ParseYtDlpHelp(string help)
    {
        ArgumentNullException.ThrowIfNull(help);

        var results = new List<HelpOptionInfo>();
        var section = "General";
        string? optionPart = null;
        var description = new List<string>();

        void Flush()
        {
            if (optionPart is null)
                return;

            var entry = CreateYtDlpEntry(
                optionPart, string.Join(' ', description), section);

            if (entry is not null)
                results.Add(entry);

            optionPart = null;
            description.Clear();
        }

        foreach (var rawLine in SplitLines(help))
        {
            var line = rawLine.TrimEnd();

            if (line.Length == 0)
                continue;

            var sectionMatch = YtDlpSectionPattern().Match(line);
            if (sectionMatch.Success)
            {
                Flush();
                section = sectionMatch.Groups["name"].Value.Trim();
                continue;
            }

            var optionMatch = YtDlpOptionPattern().Match(line);
            if (optionMatch.Success)
            {
                Flush();
                optionPart = optionMatch.Groups["opt"].Value.Trim();

                if (optionMatch.Groups["desc"].Success)
                    description.Add(optionMatch.Groups["desc"].Value.Trim());

                continue;
            }

            // Continuation lines are indented well past the option column.
            if (optionPart is not null && line.StartsWith("        "))
                description.Add(line.Trim());
        }

        Flush();
        return results;
    }

    private static HelpOptionInfo? CreateYtDlpEntry(
        string optionPart,
        string description,
        string section)
    {
        var switches = new List<string>();
        string? placeholder = null;

        foreach (var token in optionPart.Split(", "))
        {
            var trimmed = token.Trim();
            if (!trimmed.StartsWith('-'))
                continue;

            var space = trimmed.IndexOf(' ');
            if (space < 0)
            {
                switches.Add(trimmed);
            }
            else
            {
                switches.Add(trimmed[..space]);
                placeholder = trimmed[(space + 1)..].Trim();
            }
        }

        if (switches.Count == 0)
            return null;

        foreach (Match aliasMatch in YtDlpAliasPattern().Matches(description))
        {
            switches.AddRange(
                aliasMatch.Groups["aliases"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries)
                    .Where(alias => alias.StartsWith('-')));
        }

        var primary = switches.FirstOrDefault(s => s.StartsWith("--"))
                      ?? switches[0];

        var aliases = switches
            .Where(s => !string.Equals(s, primary, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new HelpOptionInfo(
            ToolNames.YtDlp,
            primary,
            aliases,
            placeholder,
            description,
            section);
    }

    public static IReadOnlyList<HelpOptionInfo> ParseFfmpegHelp(string help)
    {
        ArgumentNullException.ThrowIfNull(help);

        var results = new Dictionary<string, HelpOptionInfo>(
            StringComparer.Ordinal);
        var section = "General";
        var inAvCodecContext = false;

        foreach (var rawLine in SplitLines(help))
        {
            var line = rawLine.TrimEnd();

            if (line.Length == 0)
                continue;

            if (!char.IsWhiteSpace(line[0]) && line.EndsWith(':'))
            {
                if (string.Equals(line, "AVCodecContext AVOptions:", StringComparison.Ordinal))
                {
                    inAvCodecContext = true;
                    section = "AVCodecContext AVOptions";
                    continue;
                }

                if (inAvCodecContext)
                {
                    // Stop once per-codec / per-muxer AVOptions begin in "-h full".
                    break;
                }

                section = line.TrimEnd(':').Trim();
                continue;
            }

            if (inAvCodecContext)
            {
                var avMatch = FfmpegAvOptionPattern().Match(line);
                if (!avMatch.Success)
                    continue;

                var avName = avMatch.Groups["sw"].Value;
                results.TryAdd(avName, new HelpOptionInfo(
                    ToolNames.Ffmpeg,
                    avName,
                    Array.Empty<string>(),
                    avMatch.Groups["ph"].Value,
                    avMatch.Groups["desc"].Value.Trim(),
                    section));
                continue;
            }

            if (!line.StartsWith('-'))
                continue;

            var match = FfmpegOptionPattern().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["sw"].Value;
            if (name == "-")
                continue;

            var placeholder = match.Groups["ph"].Success
                ? match.Groups["ph"].Value
                : null;

            var description = match.Groups["desc"].Success
                ? match.Groups["desc"].Value.Trim()
                : string.Empty;

            if (match.Groups["spec"].Success)
            {
                description = string.IsNullOrEmpty(description)
                    ? "Accepts a stream specifier, e.g. :a for audio."
                    : $"{description} (accepts a stream specifier, e.g. {name}:a)";
            }

            results.TryAdd(name, new HelpOptionInfo(
                ToolNames.Ffmpeg,
                name,
                Array.Empty<string>(),
                placeholder,
                description,
                section));
        }

        return results.Values.ToArray();
    }

    public static IReadOnlyList<FfmpegEncoderInfo> ParseFfmpegEncoders(
        string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return SplitLines(output)
            .Select(line => FfmpegEncoderPattern().Match(line.TrimEnd()))
            .Where(match => match.Success &&
                            match.Groups["name"].Value != "=")
            .Select(match => new FfmpegEncoderInfo(
                match.Groups["name"].Value,
                match.Groups["type"].Value[0],
                match.Groups["desc"].Value.Trim()))
            .ToArray();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n');
}
