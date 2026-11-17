namespace FetchForge.Services;

public interface IUiHostServices
{
    Task<string?> PickFolderAsync(string title, string? initialPath = null);
    Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> patterns);
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string defaultExtension, string filterName, IReadOnlyList<string> patterns);
    Task CopyTextAsync(string text);
}
