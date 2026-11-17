using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FetchForge.Services;
using FetchForge.ViewModels;

namespace FetchForge;

public partial class MainWindow : Window, IUiHostServices
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(this);
        DataContext = _viewModel;
        Opened += async (_, _) => await _viewModel.InitializeAsync();
    }

    public async Task<string?> PickFolderAsync(
        string title,
        string? initialPath = null)
    {
        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                new Uri(Path.GetFullPath(initialPath)));
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation
            });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> PickOpenFileAsync(
        string title,
        string filterName,
        IReadOnlyList<string> patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(filterName) { Patterns = patterns }
                ]
            });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string defaultExtension,
        string filterName,
        IReadOnlyList<string> patterns)
    {
        var file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = defaultExtension,
                FileTypeChoices =
                [
                    new FilePickerFileType(filterName) { Patterns = patterns }
                ]
            });

        return file?.Path.LocalPath;
    }

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(text);
    }
}
