using FetchForge.Core.Models;
using FetchForge.Core.Services;

namespace FetchForge.Core.Tests;

public sealed class RegistryAndMatcherTests
{
    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    [Fact]
    public void DefaultRegistry_ContainsValidUniqueDefinitionsForBothTools()
    {
        var registry = new ToolOptionRegistry();

        Assert.True(registry.GetByTool(ToolNames.YtDlp).Count >= 20);
        Assert.True(registry.GetByTool(ToolNames.Ffmpeg).Count >= 9);

        var ids = registry.All.Select(d => d.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Matcher_VerifiesCuratedOptionsAgainstRealFixtures()
    {
        var ytHelp = ToolHelpParser.ParseYtDlpHelp(ReadFixture("yt-dlp-help.txt"));
        var ffHelp = ToolHelpParser.ParseFfmpegHelp(
            ReadFixture("ffmpeg-help-long.txt") +
            "\nAVCodecContext AVOptions:\n  -threads           <int>        ED.VA...... set the number of threads\n");
        var ffEncoders = ToolHelpParser.ParseFfmpegEncoders(ReadFixture("ffmpeg-encoders.txt"));

        var ytCap = new ToolCapabilityResult(
            ToolNames.YtDlp,
            true,
            "yt-dlp.exe",
            "2026.10.21",
            ytHelp.SelectMany(o => o.AllSwitches).Distinct().ToArray(),
            "OK")
        {
            HelpOptions = ytHelp,
            HelpAvailable = true
        };

        var ffCap = new ToolCapabilityResult(
            ToolNames.Ffmpeg,
            true,
            "ffmpeg.exe",
            "7.0",
            ffHelp.SelectMany(o => o.AllSwitches).Distinct().ToArray(),
            "OK")
        {
            HelpOptions = ffHelp,
            Encoders = ffEncoders,
            HelpAvailable = true
        };

        var registry = new ToolOptionRegistry();
        var matcher = new ToolOptionCapabilityMatcher(registry);
        var matches = matcher.Match([ytCap, ffCap]).ToDictionary(m => m.Definition.Id);

        Assert.Equal(ToolOptionSupportStatus.Supported, matches["yt-dlp.format"].Status);
        Assert.Equal(ToolOptionSupportStatus.Supported, matches["yt-dlp.yes-playlist"].Status);
        Assert.Equal(ToolOptionSupportStatus.Supported, matches["ffmpeg.audio-codec"].Status);
        Assert.Equal(ToolOptionSupportStatus.Supported, matches["ffmpeg.copy-metadata"].Status);
        Assert.Equal(ToolOptionSupportStatus.NotApplicable, matches["ffmpeg.output-extension"].Status);
        Assert.Equal(ToolOptionSupportStatus.Supported, matches["ffmpeg.threads"].Status);
    }

    [Fact]
    public void Matcher_ReportsUnsupportedAndUnverifiedCorrectly()
    {
        var registry = new ToolOptionRegistry(
        [
            new ToolOptionDefinition
            {
                Id = "yt-dlp.fake",
                Tool = ToolNames.YtDlp,
                DisplayName = "Fake",
                Description = "Does not exist.",
                ValueType = ToolOptionValueType.Boolean,
                CommandLineArgument = "--nonexistent-flag"
            },
            new ToolOptionDefinition
            {
                Id = "ffmpeg.check-missing",
                Tool = ToolNames.Ffmpeg,
                DisplayName = "Missing tool",
                Description = "Tool missing.",
                ValueType = ToolOptionValueType.Boolean,
                CommandLineArgument = "-vn"
            }
        ]);

        var ytCap = new ToolCapabilityResult(
            ToolNames.YtDlp, true, "yt-dlp", "1.0", ["--format"], "OK")
        {
            HelpAvailable = true
        };

        var matcher = new ToolOptionCapabilityMatcher(registry);
        var matches = matcher.Match([ytCap]).ToDictionary(m => m.Definition.Id);

        Assert.Equal(ToolOptionSupportStatus.Unsupported, matches["yt-dlp.fake"].Status);
        Assert.Equal(ToolOptionSupportStatus.Unverified, matches["ffmpeg.check-missing"].Status);
    }
}
