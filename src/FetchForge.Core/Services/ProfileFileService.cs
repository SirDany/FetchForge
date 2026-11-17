using System.Text.Json;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class ProfileFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ProfilesDirectory { get; }

    public ProfileFileService(string? profilesDirectory = null)
    {
        ProfilesDirectory = string.IsNullOrWhiteSpace(profilesDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FetchForge",
                "profiles")
            : Path.GetFullPath(profilesDirectory);
    }

    public async Task<FetchForgeProfile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The profile file is empty or invalid.");

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The profile file root must be a JSON object.");

        if (IsV2Schema(document.RootElement))
        {
            var profile = JsonSerializer.Deserialize<FetchForgeProfile>(json, JsonOptions)
                ?? throw new InvalidDataException("The profile file is empty or invalid.");
            return profile.Normalize();
        }

        var legacy = JsonSerializer.Deserialize<LegacyAudioProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("The profile file is empty or invalid.");

        return MigrateLegacyProfile(legacy, Path.GetFileNameWithoutExtension(path));
    }

    public async Task SaveAsync(
        string path,
        FetchForgeProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(profile);

        var normalized = profile.Clone().Normalize();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, normalized, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    public async Task<IReadOnlyList<FetchForgeProfile>> LoadAllFromStoreAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ProfilesDirectory);

        var files = Directory.GetFiles(ProfilesDirectory, "*.json");
        if (files.Length == 0)
        {
            var seeded = CreateBuiltInProfiles();
            foreach (var profile in seeded)
                await SaveToStoreAsync(profile, cancellationToken);
            return seeded;
        }

        var loaded = new List<FetchForgeProfile>();
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                loaded.Add(await LoadAsync(file, cancellationToken));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                // Skip unreadable profile files rather than blocking app startup.
            }
        }

        if (!loaded.Any(p => string.Equals(
                p.Name, FetchForgeProfile.DefaultName, StringComparison.OrdinalIgnoreCase)))
        {
            var defaultProfile = new FetchForgeProfile();
            await SaveToStoreAsync(defaultProfile, cancellationToken);
            loaded.Insert(0, defaultProfile);
        }

        return loaded
            .OrderBy(p => string.Equals(
                p.Name, FetchForgeProfile.DefaultName, StringComparison.OrdinalIgnoreCase)
                ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> SaveToStoreAsync(
        FetchForgeProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var normalized = profile.Clone().Normalize();
        var path = GetStoreFilePath(normalized.Name);
        await SaveAsync(path, normalized, cancellationToken);
        return path;
    }

    public bool DeleteFromStore(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) ||
            string.Equals(
                profileName.Trim(),
                FetchForgeProfile.DefaultName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = GetStoreFilePath(profileName);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    public string GetStoreFilePath(string profileName) =>
        Path.Combine(ProfilesDirectory, $"{SanitizeFileName(profileName)}.json");

    public static IReadOnlyList<FetchForgeProfile> CreateBuiltInProfiles()
    {
        var defaultProfile = new FetchForgeProfile();

        var flac = new FetchForgeProfile
        {
            Name = "Archival FLAC"
        };
        flac.Ffmpeg.Options["ffmpeg.audio-codec"] = "flac";
        flac.Ffmpeg.Options["ffmpeg.output-extension"] = "flac";
        flac.Ffmpeg.Options["ffmpeg.filter-chain"] = string.Empty;

        var raw = new FetchForgeProfile
        {
            Name = "Raw Download (No FFmpeg)"
        };
        raw.Ffmpeg.Enabled = false;
        raw.YtDlp.Options["yt-dlp.format"] = "bv*+ba/b";
        raw.YtDlp.Options["yt-dlp.extract-audio"] = "false";

        return [defaultProfile, flac, raw];
    }

    private static bool IsV2Schema(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out var version) &&
                version >= FetchForgeProfile.CurrentSchemaVersion)
            {
                return true;
            }

            if (string.Equals(property.Name, "ytDlp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static FetchForgeProfile MigrateLegacyProfile(
        LegacyAudioProfile legacy,
        string fallbackName)
    {
        var profile = new FetchForgeProfile
        {
            Name = string.IsNullOrWhiteSpace(legacy.Name)
                ? fallbackName
                : legacy.Name.Trim()
        };

        profile.Ffmpeg.Enabled = legacy.EnhancementEnabled ?? legacy.Enabled ?? true;
        profile.Ffmpeg.MaxConcurrentJobs =
            legacy.MaxParallelFfmpegJobs ?? FfmpegProfileSettings.DefaultConcurrentJobs;

        var filter = legacy.FfmpegFilter ?? legacy.FilterChain;
        if (filter is not null)
            profile.Ffmpeg.Options["ffmpeg.filter-chain"] = filter;

        if (!string.IsNullOrWhiteSpace(legacy.OutputCodec))
            profile.Ffmpeg.Options["ffmpeg.audio-codec"] = legacy.OutputCodec.Trim();

        if (!string.IsNullOrWhiteSpace(legacy.OutputQuality))
            profile.Ffmpeg.Options["ffmpeg.audio-quality"] = legacy.OutputQuality.Trim();

        if (!string.IsNullOrWhiteSpace(legacy.YtDlpArguments))
        {
            profile.YtDlp.ExtraArguments.AddRange(
                OptionValueFormatter.SplitList(legacy.YtDlpArguments));
        }

        if (!string.IsNullOrWhiteSpace(legacy.FfmpegArguments))
        {
            profile.Ffmpeg.ExtraArguments.AddRange(
                OptionValueFormatter.SplitList(legacy.FfmpegArguments));
        }

        return profile.Normalize();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned)
            ? FetchForgeProfile.DefaultName
            : cleaned;
    }
}
