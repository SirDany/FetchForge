using FetchForge.Core.Models;
using FetchForge.Core.Services;

namespace FetchForge.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public PersistenceTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), $"FetchForge_Persist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ProfileFileService_RoundTripsV2ProfileAndSeedsStore()
    {
        var storeDir = Path.Combine(_tempDir, "profiles");
        var service = new ProfileFileService(storeDir);

        var seeded = await service.LoadAllFromStoreAsync();
        Assert.Contains(seeded, p => p.Name == "Default");
        Assert.Contains(seeded, p => p.Name == "Archival FLAC");
        Assert.Contains(seeded, p => p.Name == "Raw Download (No FFmpeg)");

        var custom = new FetchForgeProfile
        {
            Name = "Podcast",
            PreservePlaylistOrder = false,
            OverwriteExisting = true
        };
        custom.YtDlp.Options["yt-dlp.limit-rate"] = "2M";
        custom.YtDlp.ExtraArguments.Add("--no-warnings");
        custom.Ffmpeg.MaxConcurrentJobs = 4;
        custom.Ffmpeg.Options["ffmpeg.audio-codec"] = "libopus";

        var savedPath = await service.SaveToStoreAsync(custom);
        var loaded = await service.LoadAsync(savedPath);

        Assert.Equal("Podcast", loaded.Name);
        Assert.False(loaded.PreservePlaylistOrder);
        Assert.True(loaded.OverwriteExisting);
        Assert.Equal("2M", loaded.YtDlp.Options["yt-dlp.limit-rate"]);
        Assert.Equal(["--no-warnings"], loaded.YtDlp.ExtraArguments);
        Assert.Equal(4, loaded.Ffmpeg.MaxConcurrentJobs);
        Assert.Equal("libopus", loaded.Ffmpeg.Options["ffmpeg.audio-codec"]);
    }

    [Fact]
    public async Task ProfileFileService_MigratesLegacyAudioProfileAndDefaultJson()
    {
        var service = new ProfileFileService(_tempDir);

        var legacyAudioPath = Path.Combine(_tempDir, "legacy-audio.json");
        await File.WriteAllTextAsync(legacyAudioPath, """
        {
          "Name": "Warm",
          "Enabled": false,
          "FilterChain": "loudnorm=I=-16",
          "OutputCodec": "aac",
          "OutputQuality": "4"
        }
        """);

        var migratedAudio = await service.LoadAsync(legacyAudioPath);
        Assert.Equal(2, migratedAudio.SchemaVersion);
        Assert.Equal("Warm", migratedAudio.Name);
        Assert.False(migratedAudio.Ffmpeg.Enabled);
        Assert.Equal("loudnorm=I=-16", migratedAudio.Ffmpeg.Options["ffmpeg.filter-chain"]);
        Assert.Equal("aac", migratedAudio.Ffmpeg.Options["ffmpeg.audio-codec"]);
        Assert.Equal("4", migratedAudio.Ffmpeg.Options["ffmpeg.audio-quality"]);

        var legacyDefaultPath = Path.Combine(_tempDir, "legacy-default.json");
        await File.WriteAllTextAsync(legacyDefaultPath, """
        {
          "Name": "Default",
          "EnhancementEnabled": true,
          "MaxParallelFfmpegJobs": 8,
          "FfmpegFilter": "highpass=f=60",
          "YtDlpArguments": "--retries\n3",
          "FfmpegArguments": "-ar\n48000"
        }
        """);

        var migratedDefault = await service.LoadAsync(legacyDefaultPath);
        Assert.True(migratedDefault.Ffmpeg.Enabled);
        Assert.Equal(8, migratedDefault.Ffmpeg.MaxConcurrentJobs);
        Assert.Equal("highpass=f=60", migratedDefault.Ffmpeg.Options["ffmpeg.filter-chain"]);
        Assert.Equal(["--retries", "3"], migratedDefault.YtDlp.ExtraArguments);
        Assert.Equal(["-ar", "48000"], migratedDefault.Ffmpeg.ExtraArguments);
    }

    [Fact]
    public async Task SettingsService_RoundTripsAndFallsBackOnMissingOrCorrupt()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(path);

        var missing = await service.LoadAsync();
        Assert.Equal(FetchForgeProfile.DefaultName, missing.LastProfileName);

        var updated = new AppSettings
        {
            OutputDirectory = @"D:\Music",
            TemporaryDirectory = @"D:\TempFF",
            LastProfileName = "Podcast",
            ShowAdvancedOptions = true,
            ToolPaths = new ToolPaths { Ffmpeg = @"C:\tools\ffmpeg.exe" }
        };

        await service.SaveAsync(updated);
        var loaded = await service.LoadAsync();

        Assert.Equal(@"D:\Music", loaded.OutputDirectory);
        Assert.Equal(@"D:\TempFF", loaded.TemporaryDirectory);
        Assert.Equal("Podcast", loaded.LastProfileName);
        Assert.True(loaded.ShowAdvancedOptions);
        Assert.Equal(@"C:\tools\ffmpeg.exe", loaded.ToolPaths.Ffmpeg);

        await File.WriteAllTextAsync(path, "{ invalid json");
        var fallback = await service.LoadAsync();
        Assert.Equal(FetchForgeProfile.DefaultName, fallback.LastProfileName);
    }
}
