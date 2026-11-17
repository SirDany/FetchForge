using System.Text.Json;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsFilePath { get; }

    public SettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FetchForge",
                "settings.json")
            : Path.GetFullPath(settingsFilePath);
    }

    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
            return new AppSettings();

        try
        {
            await using var stream = File.OpenRead(SettingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream, JsonOptions, cancellationToken);

            if (settings is null)
                return new AppSettings();

            settings.ToolPaths ??= new ToolPaths();
            if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
                settings.OutputDirectory = new AppSettings().OutputDirectory;
            if (string.IsNullOrWhiteSpace(settings.TemporaryDirectory))
                settings.TemporaryDirectory = new AppSettings().TemporaryDirectory;
            if (string.IsNullOrWhiteSpace(settings.LastProfileName))
                settings.LastProfileName = FetchForgeProfile.DefaultName;

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
