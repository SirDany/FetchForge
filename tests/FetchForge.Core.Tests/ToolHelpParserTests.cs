using FetchForge.Core.Services;

namespace FetchForge.Core.Tests;

public sealed class ToolHelpParserTests
{
    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    [Fact]
    public void ParseYtDlpHelp_ExtractsOptionsAliasesSectionsAndDescriptions()
    {
        var help = ReadFixture("yt-dlp-help.txt");
        var options = ToolHelpParser.ParseYtDlpHelp(help);

        Assert.True(options.Count > 100);

        var format = Assert.Single(options, o => o.Switch == "--format");
        Assert.Contains("-f", format.Aliases);
        Assert.Equal("FORMAT", format.ValuePlaceholder);
        Assert.Contains("Video format", format.Description);
        Assert.Equal("Video Format Options", format.Section);

        var extractAudio = Assert.Single(options, o => o.Switch == "--extract-audio");
        Assert.Contains("-x", extractAudio.Aliases);
        Assert.Null(extractAudio.ValuePlaceholder);

        var embedMetadata = Assert.Single(options, o => o.Switch == "--embed-metadata");
        Assert.Contains("--add-metadata", embedMetadata.Aliases);

        var ignoreErrors = Assert.Single(options, o => o.Switch == "--ignore-errors");
        Assert.Contains("-i", ignoreErrors.Aliases);
    }

    [Fact]
    public void ParseFfmpegHelp_ExtractsMainAndAudioOptions()
    {
        var help = ReadFixture("ffmpeg-help-long.txt");
        var options = ToolHelpParser.ParseFfmpegHelp(help);

        Assert.True(options.Count > 50);

        var af = Assert.Single(options, o => o.Switch == "-af");
        Assert.Equal("filter_graph", af.ValuePlaceholder);
        Assert.Contains("filter:a", af.Description);

        var codec = Assert.Single(options, o => o.Switch == "-c");
        Assert.Equal("codec", codec.ValuePlaceholder);
        Assert.Contains("stream specifier", codec.Description, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(options, o => o.Switch == "-q");
        Assert.Contains(options, o => o.Switch == "-ar");
        Assert.Contains(options, o => o.Switch == "-ac");
        Assert.Contains(options, o => o.Switch == "-vn");
        Assert.Contains(options, o => o.Switch == "-map_metadata");
    }

    [Fact]
    public void ParseFfmpegHelp_ExtractsAvCodecContextOptionsAndStopsBeforeCodecSpecific()
    {
        const string sample = """
            Global options (affect whole program instead of just one file):
            -y                  overwrite output files

            AVCodecContext AVOptions:
              -threads           <int>        ED.VA...... set the number of threads (from 0 to INT_MAX) (default 1)

            amv encoder AVOptions:
              -fake_amv_opt      <int>        E..V....... should not be parsed
            """;

        var options = ToolHelpParser.ParseFfmpegHelp(sample);

        Assert.Contains(options, o => o.Switch == "-y");
        var threads = Assert.Single(options, o => o.Switch == "-threads");
        Assert.Equal("AVCodecContext AVOptions", threads.Section);
        Assert.DoesNotContain(options, o => o.Switch == "-fake_amv_opt");
    }

    [Fact]
    public void ParseFfmpegEncoders_ExtractsAudioEncoders()
    {
        var output = ReadFixture("ffmpeg-encoders.txt");
        var encoders = ToolHelpParser.ParseFfmpegEncoders(output);

        var audio = encoders.Where(e => e.MediaType == 'A').Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("libmp3lame", audio);
        Assert.Contains("aac", audio);
        Assert.Contains("libopus", audio);
        Assert.Contains("flac", audio);
        Assert.Contains("pcm_s16le", audio);
        Assert.DoesNotContain("=", audio);
    }
}
