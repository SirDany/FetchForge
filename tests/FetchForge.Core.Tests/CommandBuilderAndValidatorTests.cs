using FetchForge.Core.Models;
using FetchForge.Core.Services;

namespace FetchForge.Core.Tests;

public sealed class CommandBuilderAndValidatorTests
{
    private readonly ToolOptionRegistry _registry = new();
    private readonly CommandBuilder _builder;

    public CommandBuilderAndValidatorTests()
    {
        _builder = new CommandBuilder(_registry);
    }

    [Fact]
    public void BuildYtDlpCommand_SkipsAudioExtractionWhenFfmpegEnabled_AndIncludesWhenDisabled()
    {
        var profile = new FetchForgeProfile();
        profile.Ffmpeg.Enabled = true;

        var withFfmpeg = _builder.BuildYtDlpCommand(
            profile, "https://example.com/v", @"C:\temp");

        Assert.DoesNotContain("--extract-audio", withFfmpeg.Arguments);
        Assert.DoesNotContain("--audio-format", withFfmpeg.Arguments);

        profile.Ffmpeg.Enabled = false;
        var withoutFfmpeg = _builder.BuildYtDlpCommand(
            profile, "https://example.com/v", @"C:\temp");

        Assert.Contains("--extract-audio", withoutFfmpeg.Arguments);
        Assert.Contains("--audio-format", withoutFfmpeg.Arguments);
    }

    [Fact]
    public void BuildYtDlpCommand_PrefixesPlaylistIndexOnFilenamePortionOnly()
    {
        var profile = new FetchForgeProfile { PreservePlaylistOrder = true };
        profile.YtDlp.Options["yt-dlp.output-template"] = "%(playlist)s/%(title)s.%(ext)s";

        var result = _builder.BuildYtDlpCommand(profile, "https://example.com/p", @"C:\temp");
        var outIndex = result.Arguments.ToList().IndexOf("--output");

        Assert.True(outIndex >= 0);
        Assert.Equal(
            "%(playlist)s/%(playlist_index&{0:03d} - |)s%(title)s.%(ext)s",
            result.Arguments[outIndex + 1]);
    }

    [Fact]
    public void ExtraArguments_ReservedSwitchProducesErrorAndIsOmitted_ConflictProducesWarning()
    {
        var profile = new FetchForgeProfile();
        profile.YtDlp.ExtraArguments.AddRange(["--output=bad.mp3", "-f", "best"]);

        var result = _builder.BuildYtDlpCommand(profile, "https://example.com/v", @"C:\temp");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i =>
            i.Severity == OptionValidationSeverity.Error &&
            i.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("--output=bad.mp3", result.Arguments);

        Assert.Contains(result.Issues, i =>
            i.Severity == OptionValidationSeverity.Warning &&
            i.OptionId == "yt-dlp.format");
        Assert.Contains("-f", result.Arguments);
    }

    [Fact]
    public void BuildFfmpegCommand_SkipsVbrQualityWhenBitrateIsSet_OrForLosslessCodec()
    {
        var profile = new FetchForgeProfile();
        profile.Ffmpeg.Options["ffmpeg.audio-bitrate"] = "192k";

        var withBitrate = _builder.BuildFfmpegCommand(profile, "in.webm", "out.mp3");
        Assert.DoesNotContain("-q:a", withBitrate.Arguments);
        Assert.Contains("-b:a", withBitrate.Arguments);

        var flacProfile = new FetchForgeProfile();
        flacProfile.Ffmpeg.Options["ffmpeg.audio-codec"] = "flac";

        var flacCmd = _builder.BuildFfmpegCommand(flacProfile, "in.webm", "out.flac");
        Assert.DoesNotContain("-q:a", flacCmd.Arguments);
        Assert.Equal("flac", _builder.ResolveOutputExtension(flacProfile));
    }

    [Fact]
    public void Validator_ReportsOutOfRangeNumbersInvalidEnumsAndMissingEncoder()
    {
        var validator = new OptionValueValidator(_registry);
        var profile = new FetchForgeProfile();
        profile.YtDlp.Options["yt-dlp.retries"] = "500";
        profile.Ffmpeg.Options["ffmpeg.audio-codec"] = "unknown_codec";

        var capabilities = new[]
        {
            new ToolCapabilityResult(
                ToolNames.Ffmpeg, true, "ffmpeg", "7.0", ["-c", "-af"], "OK")
            {
                HelpAvailable = true,
                Encoders = [new FfmpegEncoderInfo("libmp3lame", 'A', "MP3")]
            }
        };

        var issues = validator.ValidateProfile(profile, capabilities);

        Assert.Contains(issues, i =>
            i.OptionId == "yt-dlp.retries" && i.Severity == OptionValidationSeverity.Error);
        Assert.Contains(issues, i =>
            i.OptionId == "ffmpeg.audio-codec" && i.Severity == OptionValidationSeverity.Error);
        Assert.Contains(issues, i =>
            i.OptionId == "ffmpeg.audio-codec" && i.Severity == OptionValidationSeverity.Warning);
    }
}
