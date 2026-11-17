using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

/// <summary>
/// Builds structured <c>argv</c> lists and live command previews for
/// <c>yt-dlp</c> and <c>ffmpeg</c>.
/// </summary>
public sealed class CommandBuilder
{
    private static readonly HashSet<string> YtDlpReservedSwitches =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--newline",
            "--progress",
            "--progress-template",
            "--print",
            "-O",
            "-P",
            "--paths",
            "-o",
            "--output"
        };

    private static readonly HashSet<string> FfmpegReservedSwitches =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "-i",
            "-y",
            "-n",
            "-progress",
            "-stats_period",
            "-nostdin",
            "-hide_banner"
        };

    private static readonly Dictionary<string, string> YtDlpSwitchAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["-f"] = "--format",
            ["-x"] = "--extract-audio",
            ["-i"] = "--ignore-errors",
            ["-R"] = "--retries",
            ["-N"] = "--concurrent-fragments",
            ["-r"] = "--limit-rate",
            ["--ies"] = "--use-extractors",
            ["--no-config"] = "--ignore-config",
            ["--no-playlist"] = "--yes-playlist",
            ["--no-embed-metadata"] = "--embed-metadata"
        };

    private static readonly Dictionary<string, string> FfmpegSwitchAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["-acodec"] = "-c:a",
            ["-codec:a"] = "-c:a",
            ["-ab"] = "-b:a",
            ["-aq"] = "-q:a",
            ["-filter:a"] = "-af"
        };

    private static readonly HashSet<string> LosslessOrBitrateOnlyCodecs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "flac",
            "alac",
            "pcm_s16le",
            "pcm_s24le",
            "libopus"
        };

    private readonly ToolOptionRegistry _registry;
    private readonly OptionValueValidator _validator;

    public CommandBuilder(
        ToolOptionRegistry registry,
        OptionValueValidator? validator = null)
    {
        _registry = registry
            ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? new OptionValueValidator(_registry);
    }

    public CommandBuildResult BuildYtDlpCommand(
        FetchForgeProfile profile,
        string url,
        string workingDirectory,
        ToolPaths? toolPaths = null,
        IEnumerable<ToolCapabilityResult>? capabilities = null,
        bool forExecution = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var executable = (toolPaths ?? new ToolPaths()).Resolve(ToolNames.YtDlp);
        var issues = _validator.ValidateProfile(profile, capabilities)
            .Where(issue => string.Equals(
                issue.Tool, ToolNames.YtDlp, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var arguments = new List<string>();
        var emittedCuratedSwitches = new Dictionary<string, ToolOptionDefinition>(
            StringComparer.OrdinalIgnoreCase);

        if (forExecution)
        {
            arguments.AddRange([
                "--newline",
                "--progress",
                "--no-quiet",
                "--no-simulate",
                "--progress-template",
                "download:ff-progress:%(info.id)s|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(info.title)s",
                "--print",
                "after_move:ff-file:%(filepath)s",
                "-P",
                workingDirectory
            ]);
        }

        foreach (var definition in _registry.GetByTool(ToolNames.YtDlp))
        {
            if (definition.AppliesWhen == OptionApplicability.WhenProcessingDisabled &&
                profile.Ffmpeg.Enabled)
            {
                continue;
            }

            if (definition.Id == "yt-dlp.output-template")
            {
                var template = profile.GetValue(definition);
                if (string.IsNullOrWhiteSpace(template))
                    template = "%(title)s.%(ext)s";

                var effectiveTemplate = ApplyPlaylistOrderTemplate(
                    template.Trim(), profile.PreservePlaylistOrder);

                arguments.Add("--output");
                arguments.Add(effectiveTemplate);
                TrackEmittedSwitches(
                    definition, ["--output"], YtDlpSwitchAliases, emittedCuratedSwitches);
                continue;
            }

            EmitOptionArguments(
                definition, profile, arguments, YtDlpSwitchAliases, emittedCuratedSwitches);
        }

        AppendExtraArguments(
            ToolNames.YtDlp,
            profile.YtDlp.ExtraArguments,
            YtDlpReservedSwitches,
            YtDlpSwitchAliases,
            emittedCuratedSwitches,
            arguments,
            issues);

        arguments.Add("--");
        arguments.Add(string.IsNullOrWhiteSpace(url) ? "<URL>" : url.Trim());

        return new CommandBuildResult(executable, arguments, issues);
    }

    public CommandBuildResult BuildFfmpegCommand(
        FetchForgeProfile profile,
        string inputPath,
        string outputPath,
        ToolPaths? toolPaths = null,
        IEnumerable<ToolCapabilityResult>? capabilities = null,
        bool forExecution = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var executable = (toolPaths ?? new ToolPaths()).Resolve(ToolNames.Ffmpeg);
        var issues = _validator.ValidateProfile(profile, capabilities)
            .Where(issue => string.Equals(
                issue.Tool, ToolNames.Ffmpeg, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            forExecution || profile.OverwriteExisting ? "-y" : "-n"
        };

        if (forExecution)
        {
            arguments.AddRange(["-progress", "pipe:1", "-stats_period", "0.25"]);
        }

        arguments.Add("-i");
        arguments.Add(string.IsNullOrWhiteSpace(inputPath) ? "<input>" : inputPath);

        var codec = _registry.TryGet("ffmpeg.audio-codec", out var codecDef) && codecDef is not null
            ? profile.GetValue(codecDef)?.Trim()
            : null;

        var bitrate = _registry.TryGet("ffmpeg.audio-bitrate", out var bitrateDef) && bitrateDef is not null
            ? profile.GetValue(bitrateDef)?.Trim()
            : null;

        var emittedCuratedSwitches = new Dictionary<string, ToolOptionDefinition>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in _registry.GetByTool(ToolNames.Ffmpeg))
        {
            if (definition.CommandLineArgument is null)
                continue;

            if (definition.Id == "ffmpeg.audio-quality")
            {
                if (!string.IsNullOrWhiteSpace(bitrate))
                    continue;

                if (!string.IsNullOrWhiteSpace(codec) &&
                    LosslessOrBitrateOnlyCodecs.Contains(codec) &&
                    !profile.IsOverridden(definition))
                {
                    continue;
                }
            }

            EmitOptionArguments(
                definition, profile, arguments, FfmpegSwitchAliases, emittedCuratedSwitches);
        }

        AppendExtraArguments(
            ToolNames.Ffmpeg,
            profile.Ffmpeg.ExtraArguments,
            FfmpegReservedSwitches,
            FfmpegSwitchAliases,
            emittedCuratedSwitches,
            arguments,
            issues);

        arguments.Add(string.IsNullOrWhiteSpace(outputPath)
            ? $"<output.{ResolveOutputExtension(profile)}>"
            : outputPath);

        return new CommandBuildResult(executable, arguments, issues);
    }

    public string ResolveOutputExtension(FetchForgeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_registry.TryGet("ffmpeg.output-extension", out var extDef) &&
            extDef is not null)
        {
            var configured = profile.GetValue(extDef)?.Trim().TrimStart('.');
            if (!string.IsNullOrWhiteSpace(configured) &&
                !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return configured;
            }
        }

        if (_registry.TryGet("ffmpeg.audio-codec", out var codecDef) &&
            codecDef is not null)
        {
            var codec = profile.GetValue(codecDef)?.Trim();
            if (!string.IsNullOrWhiteSpace(codec) &&
                ToolOptionRegistry.EncoderExtensions.TryGetValue(codec, out var mapped))
            {
                return mapped;
            }
        }

        return "mp3";
    }

    private static string ApplyPlaylistOrderTemplate(
        string template,
        bool preservePlaylistOrder)
    {
        if (!preservePlaylistOrder)
            return template;

        if (template.Contains("playlist_index", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("playlist_autonumber", StringComparison.OrdinalIgnoreCase))
        {
            return template;
        }

        const string prefix = "%(playlist_index&{0:03d} - |)s";
        var lastSep = template.LastIndexOfAny(['/', '\\']);

        return lastSep >= 0
            ? $"{template[..(lastSep + 1)]}{prefix}{template[(lastSep + 1)..]}"
            : $"{prefix}{template}";
    }

    private static void EmitOptionArguments(
        ToolOptionDefinition definition,
        FetchForgeProfile profile,
        List<string> arguments,
        IReadOnlyDictionary<string, string> aliases,
        Dictionary<string, ToolOptionDefinition> emittedCuratedSwitches)
    {
        var rawValue = profile.GetValue(definition);

        switch (definition.ValueType)
        {
            case ToolOptionValueType.Boolean:
                if (!OptionValueFormatter.TryParseBoolean(rawValue, out var flag))
                    return;

                var args = flag
                    ? definition.EffectiveTrueArguments
                    : definition.EffectiveFalseArguments;

                if (args.Count == 0)
                    return;

                arguments.AddRange(args);
                TrackEmittedSwitches(definition, args, aliases, emittedCuratedSwitches);
                break;

            case ToolOptionValueType.StringList:
                if (definition.CommandLineArgument is null)
                    return;

                var items = OptionValueFormatter.SplitList(rawValue);
                if (items.Count == 0)
                    return;

                foreach (var item in items)
                {
                    arguments.Add(definition.CommandLineArgument);
                    arguments.Add(item);
                }

                TrackEmittedSwitches(
                    definition, [definition.CommandLineArgument], aliases, emittedCuratedSwitches);
                break;

            default:
                if (definition.CommandLineArgument is null ||
                    string.IsNullOrWhiteSpace(rawValue))
                {
                    return;
                }

                arguments.Add(definition.CommandLineArgument);
                arguments.Add(rawValue.Trim());
                TrackEmittedSwitches(
                    definition, [definition.CommandLineArgument], aliases, emittedCuratedSwitches);
                break;
        }
    }

    private static void TrackEmittedSwitches(
        ToolOptionDefinition definition,
        IEnumerable<string> args,
        IReadOnlyDictionary<string, string> aliases,
        Dictionary<string, ToolOptionDefinition> emittedCuratedSwitches)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith('-'))
                continue;

            var canonical = CanonicalizeSwitch(ExtractSwitchToken(arg), aliases);
            emittedCuratedSwitches.TryAdd(canonical, definition);
        }
    }

    private static void AppendExtraArguments(
        string tool,
        IEnumerable<string> extraArguments,
        HashSet<string> reservedSwitches,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, ToolOptionDefinition> emittedCuratedSwitches,
        List<string> arguments,
        List<OptionValidationIssue> issues)
    {
        foreach (var rawArg in extraArguments)
        {
            var arg = rawArg.Trim();
            if (arg.Length == 0)
                continue;

            if (arg.StartsWith('-'))
            {
                var sw = ExtractSwitchToken(arg);

                if (reservedSwitches.Contains(sw))
                {
                    issues.Add(new OptionValidationIssue(
                        tool,
                        null,
                        OptionValidationSeverity.Error,
                        $"Extra argument '{arg}' uses a reserved switch managed by FetchForge."));
                    continue;
                }

                var canonical = CanonicalizeSwitch(sw, aliases);
                if (emittedCuratedSwitches.TryGetValue(canonical, out var curatedDef))
                {
                    issues.Add(new OptionValidationIssue(
                        tool,
                        curatedDef.Id,
                        OptionValidationSeverity.Warning,
                        $"Extra argument '{arg}' overrides curated option '{curatedDef.DisplayName}'."));
                }
            }

            arguments.Add(arg);
        }
    }

    private static string ExtractSwitchToken(string argument)
    {
        var equalsIndex = argument.IndexOf('=');
        return equalsIndex > 0 ? argument[..equalsIndex] : argument;
    }

    private static string CanonicalizeSwitch(
        string switchToken,
        IReadOnlyDictionary<string, string> aliases) =>
        aliases.TryGetValue(switchToken, out var canonical)
            ? canonical
            : switchToken;
}
