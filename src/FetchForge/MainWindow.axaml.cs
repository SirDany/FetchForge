
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FetchForge.Core.Models;
using FetchForge.Core.Services;

namespace FetchForge;

public partial class MainWindow : Window
{
    private readonly DependencyChecker _dependencies = new();
    private readonly ProfileFileService _profiles = new();

    public MainWindow()
    {
        InitializeComponent();

        OutputBox.Text = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "Downloads");

        FilterBox.Text = new AudioProfile().FilterChain;

        Log("FetchForge initialized.");
        Log("Download execution is not implemented yet.");
    }

    private async void BrowseOutput_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select output directory",
                AllowMultiple = false
            });

        if (folders.Count > 0)
            OutputBox.Text = folders[0].Path.LocalPath;
    }

    private async void CheckTools_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        Log(await _dependencies.CheckAsync());
    }

    private async void ImportProfile_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import audio profile",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON profile")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

        if (files.Count == 0)
            return;

        try
        {
            var profile = await _profiles.LoadAsync(
                files[0].Path.LocalPath);

            FilterBox.Text = profile.FilterChain;
            EnhanceCheck.IsChecked = profile.Enabled;

            Log($"Imported profile: {profile.Name}");
        }
        catch (Exception ex)
        {
            Log($"Profile import failed: {ex.Message}");
        }
    }

    private async void ExportProfile_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export audio profile",
                SuggestedFileName = "FetchForge-AudioProfile.json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON profile")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

        if (file is null)
            return;

        try
        {
            var profile = new AudioProfile
            {
                Name = "Custom",
                Enabled = EnhanceCheck.IsChecked ?? false,
                FilterChain = FilterBox.Text ?? string.Empty
            };

            await _profiles.SaveAsync(
                file.Path.LocalPath, profile);

            Log($"Exported profile: {file.Path.LocalPath}");
        }
        catch (Exception ex)
        {
            Log($"Profile export failed: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        LogBox.Text = string.IsNullOrWhiteSpace(LogBox.Text)
            ? line
            : $"{LogBox.Text}{Environment.NewLine}{line}";
    }
}
