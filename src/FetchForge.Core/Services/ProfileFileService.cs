
using System.Text.Json;
using FetchForge.Core.Models;

namespace FetchForge.Core.Services;

public sealed class ProfileFileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AudioProfile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);

        var profile =
            await JsonSerializer.DeserializeAsync<AudioProfile>(
                stream, Options, cancellationToken);

        return profile
            ?? throw new InvalidDataException(
                "The profile file is empty or invalid.");
    }

    public async Task SaveAsync(
        string path,
        AudioProfile profile,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(
            Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);

        await JsonSerializer.SerializeAsync(
            stream, profile, Options, cancellationToken);
    }
}
