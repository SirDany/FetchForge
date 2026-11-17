using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FetchForge.Core.Models;
using FetchForge.Core.Services;
using FetchForge.Services;

namespace FetchForge.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ToolOptionRegistry _registry = new();
    private readonly CommandBuilder _commandBuilder;
    private readonly DependencyChecker _dependencyChecker;
    private readonly ProfileFileService _profileService = new();
    private readonly SettingsService _settingsService = new();
    private readonly JobOrchestrator _orchestrator;
    private readonly IUiHostServices _uiHost;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private AppSettings _settings = new();
    private IReadOnlyList<ToolCapabilityResult> _capabilities = Array.Empty<ToolCapabilityResult>();
    private IReadOnlyList<HelpOptionInfo> _allYtDlpHelp = Array.Empty<HelpOptionInfo>();
    private IReadOnlyList<HelpOptionInfo> _allFfmpegHelp = Array.Empty<HelpOptionInfo>();
    private CancellationTokenSource? _jobCts;
    private bool _suppressUpdates;
    private bool _isInitialized;
    private string _profileEditorMode = "New";

    public MainWindowViewModel(IUiHostServices uiHost)
    {
        _uiHost = uiHost ?? throw new ArgumentNullException(nameof(uiHost));
        _commandBuilder = new CommandBuilder(_registry);
        var discovery = new ToolCapabilityDiscovery();
        _dependencyChecker = new DependencyChecker(discovery, _registry);
        _orchestrator = new JobOrchestrator(new ProcessRunner(), _registry);

        BuildOptionGroups(new FetchForgeProfile());
        RefreshCommandPreviewAndFilters();
    }

    public ObservableCollection<FetchForgeProfile> Profiles { get; } = new();
    public ObservableCollection<OptionCategoryGroupViewModel> YtDlpOptionGroups { get; } = new();
    public ObservableCollection<OptionCategoryGroupViewModel> FfmpegOptionGroups { get; } = new();
    public ObservableCollection<HelpOptionItemViewModel> FilteredYtDlpHelpOptions { get; } = new();
    public ObservableCollection<HelpOptionItemViewModel> FilteredFfmpegHelpOptions { get; } = new();
    public ObservableCollection<ValidationIssueItemViewModel> ValidationIssues { get; } = new();
    public ObservableCollection<FileProgressItemViewModel> FileItems { get; } = new();

    [ObservableProperty] private FetchForgeProfile? _selectedProfile;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _outputDirectory = string.Empty;
    [ObservableProperty] private string _temporaryDirectory = string.Empty;
    [ObservableProperty] private bool _showAdvancedOptions;
    [ObservableProperty] private bool _preservePlaylistOrder = true;
    [ObservableProperty] private bool _overwriteExisting;
    [ObservableProperty] private bool _ffmpegEnabled = true;
    [ObservableProperty] private decimal? _maxConcurrentJobs = FfmpegProfileSettings.DefaultConcurrentJobs;

    [ObservableProperty] private string _ytDlpPath = string.Empty;
    [ObservableProperty] private string _ffmpegPath = string.Empty;
    [ObservableProperty] private string _ffprobePath = string.Empty;

    [ObservableProperty] private string _ytDlpOptionSearch = string.Empty;
    [ObservableProperty] private string _ffmpegOptionSearch = string.Empty;
    [ObservableProperty] private string _ytDlpHelpSearch = string.Empty;
    [ObservableProperty] private string _ffmpegHelpSearch = string.Empty;

    [ObservableProperty] private string _ytDlpExtraArgumentsText = string.Empty;
    [ObservableProperty] private string _ffmpegExtraArgumentsText = string.Empty;

    [ObservableProperty] private string _ytDlpCommandPreview = string.Empty;
    [ObservableProperty] private string _ffmpegCommandPreview = string.Empty;
    [ObservableProperty] private bool _hasValidationIssues;
    [ObservableProperty] private bool _hasValidationErrors;

    [ObservableProperty] private string _ytDlpStatusText = "Checking yt-dlp...";
    [ObservableProperty] private string _ffmpegStatusText = "Checking FFmpeg...";
    [ObservableProperty] private string _ffprobeStatusText = "Checking ffprobe...";
    [ObservableProperty] private bool _isDiscoveringTools;
    [ObservableProperty] private string _ytDlpHelpSummary = "Run tool discovery to browse all options.";
    [ObservableProperty] private string _ffmpegHelpSummary = "Run tool discovery to browse all options.";

    [ObservableProperty] private bool _isProfileNameEditorOpen;
    [ObservableProperty] private string _profileNameEditorPrompt = "New profile name";
    [ObservableProperty] private string _profileNameInput = string.Empty;
    [ObservableProperty] private bool _canDeleteSelectedProfile;

    [ObservableProperty] private bool _isJobRunning;
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _jobStatusMessage = "Ready.";
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private string _finalReportText = string.Empty;
    [ObservableProperty] private bool _hasFinalReport;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;
        _isInitialized = true;

        AppendLog("Loading settings and profiles...");
        _settings = await _settingsService.LoadAsync();

        _suppressUpdates = true;
        try
        {
            OutputDirectory = _settings.OutputDirectory;
            TemporaryDirectory = _settings.TemporaryDirectory;
            ShowAdvancedOptions = _settings.ShowAdvancedOptions;
            YtDlpPath = _settings.ToolPaths.YtDlp ?? string.Empty;
            FfmpegPath = _settings.ToolPaths.Ffmpeg ?? string.Empty;
            FfprobePath = _settings.ToolPaths.Ffprobe ?? string.Empty;

            var loadedProfiles = await _profileService.LoadAllFromStoreAsync();
            Profiles.Clear();
            foreach (var profile in loadedProfiles)
                Profiles.Add(profile);

            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(
                p.Name, _settings.LastProfileName, StringComparison.OrdinalIgnoreCase))
                ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _suppressUpdates = false;
        }

        if (SelectedProfile is not null)
            ApplyProfileToFields(SelectedProfile);

        RefreshCommandPreviewAndFilters();
        AppendLog("FetchForge ready.");

        await CheckDependenciesAsync();
    }

    partial void OnSelectedProfileChanged(FetchForgeProfile? value)
    {
        if (value is null)
        {
            CanDeleteSelectedProfile = false;
            return;
        }

        CanDeleteSelectedProfile = !string.Equals(
            value.Name, FetchForgeProfile.DefaultName, StringComparison.OrdinalIgnoreCase);

        if (_suppressUpdates)
            return;

        ApplyProfileToFields(value);
        _settings.LastProfileName = value.Name;
        RefreshCommandPreviewAndFilters();
        QueueSaveSettings();
    }

    partial void OnUrlChanged(string value)
    {
        RefreshCommandPreviewAndFilters();
        StartJobCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputDirectoryChanged(string value)
    {
        if (_suppressUpdates) return;
        _settings.OutputDirectory = value;
        QueueSaveSettings();
    }

    partial void OnTemporaryDirectoryChanged(string value)
    {
        if (_suppressUpdates) return;
        _settings.TemporaryDirectory = value;
        QueueSaveSettings();
    }

    partial void OnShowAdvancedOptionsChanged(bool value)
    {
        if (_suppressUpdates) return;
        _settings.ShowAdvancedOptions = value;
        ApplyOptionFilters();
        QueueSaveSettings();
    }

    partial void OnYtDlpPathChanged(string value)
    {
        if (_suppressUpdates) return;
        _settings.ToolPaths.YtDlp = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        RefreshCommandPreviewAndFilters();
        QueueSaveSettings();
    }

    partial void OnFfmpegPathChanged(string value)
    {
        if (_suppressUpdates) return;
        _settings.ToolPaths.Ffmpeg = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        RefreshCommandPreviewAndFilters();
        QueueSaveSettings();
    }

    partial void OnFfprobePathChanged(string value)
    {
        if (_suppressUpdates) return;
        _settings.ToolPaths.Ffprobe = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        QueueSaveSettings();
    }

    partial void OnPreservePlaylistOrderChanged(bool value)
    {
        if (_suppressUpdates || SelectedProfile is null) return;
        SelectedProfile.PreservePlaylistOrder = value;
        OnActiveProfileEdited();
    }

    partial void OnOverwriteExistingChanged(bool value)
    {
        if (_suppressUpdates || SelectedProfile is null) return;
        SelectedProfile.OverwriteExisting = value;
        OnActiveProfileEdited();
    }

    partial void OnFfmpegEnabledChanged(bool value)
    {
        if (_suppressUpdates || SelectedProfile is null) return;
        SelectedProfile.Ffmpeg.Enabled = value;
        RefreshOptionApplicableStates(SelectedProfile);
        OnActiveProfileEdited();
    }

    partial void OnMaxConcurrentJobsChanged(decimal? value)
    {
        if (_suppressUpdates || SelectedProfile is null) return;
        var count = value.HasValue
            ? (int)Math.Round(value.Value)
            : FfmpegProfileSettings.DefaultConcurrentJobs;
        SelectedProfile.Ffmpeg.MaxConcurrentJobs = Math.Clamp(
            count, 1, FfmpegProfileSettings.MaxAllowedConcurrentJobs);
        OnActiveProfileEdited();
    }

    partial void OnYtDlpExtraArgumentsTextChanged(string value)
    {
        if (_suppressUpdates || SelectedProfile is null) return;
        SelectedProfile.YtDlp.ExtraArguments =
            OptionValueFormatter.SplitList(value).ToList();
        OnActiveProfileEdited();
    }

    partial void OnFfmpegExtraArgumentsTextChanged(string value)
    {
        if (_suppressUpdates || SelectedProfile is null) return;
        SelectedProfile.Ffmpeg.ExtraArguments =
            OptionValueFormatter.SplitList(value).ToList();
        OnActiveProfileEdited();
    }

    partial void OnYtDlpOptionSearchChanged(string value) => ApplyOptionFilters();
    partial void OnFfmpegOptionSearchChanged(string value) => ApplyOptionFilters();
    partial void OnYtDlpHelpSearchChanged(string value) => RefreshYtDlpHelpFilter();
    partial void OnFfmpegHelpSearchChanged(string value) => RefreshFfmpegHelpFilter();

    private void BuildOptionGroups(FetchForgeProfile profile)
    {
        YtDlpOptionGroups.Clear();
        foreach (var group in _registry.GetByTool(ToolNames.YtDlp)
                     .GroupBy(d => d.Category))
        {
            YtDlpOptionGroups.Add(new OptionCategoryGroupViewModel(
                group.Key,
                group.Select(d => new OptionItemViewModel(d, profile, OnActiveProfileEdited))));
        }

        FfmpegOptionGroups.Clear();
        foreach (var group in _registry.GetByTool(ToolNames.Ffmpeg)
                     .GroupBy(d => d.Category))
        {
            FfmpegOptionGroups.Add(new OptionCategoryGroupViewModel(
                group.Key,
                group.Select(d => new OptionItemViewModel(d, profile, OnActiveProfileEdited))));
        }
    }

    private void ApplyProfileToFields(FetchForgeProfile profile)
    {
        _suppressUpdates = true;
        try
        {
            PreservePlaylistOrder = profile.PreservePlaylistOrder;
            OverwriteExisting = profile.OverwriteExisting;
            FfmpegEnabled = profile.Ffmpeg.Enabled;
            MaxConcurrentJobs = profile.Ffmpeg.MaxConcurrentJobs;
            YtDlpExtraArgumentsText = string.Join(
                Environment.NewLine, profile.YtDlp.ExtraArguments);
            FfmpegExtraArgumentsText = string.Join(
                Environment.NewLine, profile.Ffmpeg.ExtraArguments);

            foreach (var item in EnumerateAllOptionItems())
                item.RefreshFromProfile(profile);
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    private void RefreshOptionApplicableStates(FetchForgeProfile profile)
    {
        foreach (var item in EnumerateAllOptionItems())
            item.RefreshFromProfile(profile);
    }

    private IEnumerable<OptionItemViewModel> EnumerateAllOptionItems() =>
        YtDlpOptionGroups.SelectMany(g => g.Options)
            .Concat(FfmpegOptionGroups.SelectMany(g => g.Options));

    private void OnActiveProfileEdited()
    {
        RefreshCommandPreviewAndFilters();
        if (SelectedProfile is not null)
            QueueSaveProfile(SelectedProfile);
    }

    private void RefreshCommandPreviewAndFilters()
    {
        var profile = SelectedProfile ?? new FetchForgeProfile();
        var outputDir = string.IsNullOrWhiteSpace(OutputDirectory)
            ? _settings.OutputDirectory
            : OutputDirectory;

        var ytCmd = _commandBuilder.BuildYtDlpCommand(
            profile,
            Url,
            outputDir,
            _settings.ToolPaths,
            _capabilities,
            forExecution: false);
        YtDlpCommandPreview = ytCmd.FormattedCommandLine;

        var ffmpegCmd = _commandBuilder.BuildFfmpegCommand(
            profile,
            "<downloaded-file>",
            $"<output.{_commandBuilder.ResolveOutputExtension(profile)}>",
            _settings.ToolPaths,
            _capabilities,
            forExecution: false);

        FfmpegCommandPreview = profile.Ffmpeg.Enabled
            ? ffmpegCmd.FormattedCommandLine
            : "(FFmpeg post-processing is disabled)";

        var allIssues = ytCmd.Issues
            .Concat(profile.Ffmpeg.Enabled ? ffmpegCmd.Issues : Array.Empty<OptionValidationIssue>())
            .ToArray();

        ValidationIssues.Clear();
        foreach (var issue in allIssues)
            ValidationIssues.Add(new ValidationIssueItemViewModel(issue));

        HasValidationIssues = ValidationIssues.Count > 0;
        HasValidationErrors = ValidationIssues.Any(i => i.IsError);

        ApplyOptionFilters();
        StartJobCommand.NotifyCanExecuteChanged();
    }

    private void ApplyOptionFilters()
    {
        foreach (var group in YtDlpOptionGroups)
            group.ApplyFilter(ShowAdvancedOptions, YtDlpOptionSearch);

        foreach (var group in FfmpegOptionGroups)
            group.ApplyFilter(ShowAdvancedOptions, FfmpegOptionSearch);
    }

    private void RefreshYtDlpHelpFilter() =>
        ApplyHelpFilter(_allYtDlpHelp, YtDlpHelpSearch, FilteredYtDlpHelpOptions, AddHelpSwitchToExtra);

    private void RefreshFfmpegHelpFilter() =>
        ApplyHelpFilter(_allFfmpegHelp, FfmpegHelpSearch, FilteredFfmpegHelpOptions, AddHelpSwitchToExtra);

    private static void ApplyHelpFilter(
        IReadOnlyList<HelpOptionInfo> source,
        string searchText,
        ObservableCollection<HelpOptionItemViewModel> target,
        Action<HelpOptionInfo> onAdd)
    {
        target.Clear();
        var query = searchText.Trim();

        var matches = string.IsNullOrEmpty(query)
            ? source
            : source.Where(o =>
                o.Switch.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                o.Section.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in matches)
            target.Add(new HelpOptionItemViewModel(entry, onAdd));
    }

    private void AddHelpSwitchToExtra(HelpOptionInfo info)
    {
        if (SelectedProfile is null)
            return;

        if (string.Equals(info.Tool, ToolNames.YtDlp, StringComparison.OrdinalIgnoreCase))
        {
            var lines = OptionValueFormatter.SplitList(YtDlpExtraArgumentsText).ToList();
            lines.Add(info.Switch);
            if (info.TakesValue)
                lines.Add($"<{info.ValuePlaceholder}>");
            YtDlpExtraArgumentsText = string.Join(Environment.NewLine, lines);
            AppendLog($"Added '{info.Switch}' to yt-dlp extra arguments.");
        }
        else if (string.Equals(info.Tool, ToolNames.Ffmpeg, StringComparison.OrdinalIgnoreCase))
        {
            var lines = OptionValueFormatter.SplitList(FfmpegExtraArgumentsText).ToList();
            lines.Add(info.Switch);
            if (info.TakesValue)
                lines.Add($"<{info.ValuePlaceholder}>");
            FfmpegExtraArgumentsText = string.Join(Environment.NewLine, lines);
            AppendLog($"Added '{info.Switch}' to FFmpeg extra arguments.");
        }
    }

    private bool CanStartJob() =>
        !IsJobRunning &&
        !string.IsNullOrWhiteSpace(Url) &&
        !HasValidationErrors;

    [RelayCommand(CanExecute = nameof(CanStartJob))]
    private async Task StartJobAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(Url))
            return;

        IsJobRunning = true;
        OverallProgress = 0;
        JobStatusMessage = "Starting job...";
        HasFinalReport = false;
        FinalReportText = string.Empty;
        FileItems.Clear();
        StartJobCommand.NotifyCanExecuteChanged();
        CancelJobCommand.NotifyCanExecuteChanged();

        var profileSnapshot = SelectedProfile.Clone();
        var settingsSnapshot = new AppSettings
        {
            OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
                ? _settings.OutputDirectory
                : OutputDirectory.Trim(),
            TemporaryDirectory = string.IsNullOrWhiteSpace(TemporaryDirectory)
                ? _settings.TemporaryDirectory
                : TemporaryDirectory.Trim(),
            LastProfileName = profileSnapshot.Name,
            ShowAdvancedOptions = ShowAdvancedOptions,
            ToolPaths = _settings.ToolPaths.Clone()
        };

        _jobCts = new CancellationTokenSource();
        var progress = new Progress<JobProgressUpdate>(HandleJobProgress);

        try
        {
            AppendLog($"Starting job for: {Url.Trim()}");
            var report = await Task.Run(() => _orchestrator.RunJobAsync(
                profileSnapshot,
                settingsSnapshot,
                Url.Trim(),
                progress,
                msg => RunOnUi(() => AppendLog(msg)),
                _capabilities,
                _jobCts.Token));

            foreach (var item in report.Items)
                UpsertFileItem(item);

            FinalReportText = report.FormatReport();
            HasFinalReport = true;
            JobStatusMessage = report.Stage switch
            {
                JobStage.Completed => $"Done: {report.SucceededCount} file(s) saved.",
                JobStage.Cancelled => "Cancelled.",
                _ => $"Finished with errors ({report.FailedCount} failed)."
            };
            OverallProgress = report.Stage == JobStage.Completed ? 100 : OverallProgress;
            AppendLog(FinalReportText);
        }
        finally
        {
            _jobCts.Dispose();
            _jobCts = null;
            IsJobRunning = false;
            StartJobCommand.NotifyCanExecuteChanged();
            CancelJobCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancelJob() => IsJobRunning;

    [RelayCommand(CanExecute = nameof(CanCancelJob))]
    private void CancelJob()
    {
        if (_jobCts is null || _jobCts.IsCancellationRequested)
            return;

        JobStatusMessage = "Cancelling...";
        AppendLog("Cancellation requested. Stopping active processes...");
        _jobCts.Cancel();
    }

    private void HandleJobProgress(JobProgressUpdate update)
    {
        JobStatusMessage = update.Message;
        OverallProgress = Math.Clamp(update.OverallPercent, 0, 100);
        if (update.Item is not null)
            UpsertFileItem(update.Item);
    }

    private void UpsertFileItem(JobFileItem item)
    {
        var existing = FileItems.FirstOrDefault(f =>
            string.Equals(f.Id, item.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.UpdateFrom(item);
        }
        else
        {
            FileItems.Add(new FileProgressItemViewModel(item));
        }
    }

    [RelayCommand]
    private async Task CheckDependenciesAsync()
    {
        if (IsDiscoveringTools)
            return;

        IsDiscoveringTools = true;
        try
        {
            var report = await _dependencyChecker.DiscoverAndMatchAsync(_settings.ToolPaths);
            _capabilities = report.Capabilities;

            UpdateToolStatusSummaries(report.Capabilities);

            var byId = report.Matches.ToDictionary(
                m => m.Definition.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var optionVm in EnumerateAllOptionItems())
            {
                if (byId.TryGetValue(optionVm.Id, out var match))
                    optionVm.UpdateCapability(match);
            }

            var ytCap = report.Capabilities.FirstOrDefault(c => c.ToolName == ToolNames.YtDlp);
            _allYtDlpHelp = ytCap?.HelpOptions ?? Array.Empty<HelpOptionInfo>();
            YtDlpHelpSummary = ytCap is { Found: true, HelpAvailable: true }
                ? $"{_allYtDlpHelp.Count} options parsed from yt-dlp --help"
                : ytCap?.Message ?? "yt-dlp not available.";
            RefreshYtDlpHelpFilter();

            var ffCap = report.Capabilities.FirstOrDefault(c => c.ToolName == ToolNames.Ffmpeg);
            _allFfmpegHelp = ffCap?.HelpOptions ?? Array.Empty<HelpOptionInfo>();
            FfmpegHelpSummary = ffCap is { Found: true, HelpAvailable: true }
                ? $"{_allFfmpegHelp.Count} options parsed from ffmpeg -h full ({ffCap.Encoders.Count} encoders)"
                : ffCap?.Message ?? "FFmpeg not available.";
            RefreshFfmpegHelpFilter();

            RefreshCommandPreviewAndFilters();
            AppendLog(report.SummaryText);
        }
        finally
        {
            IsDiscoveringTools = false;
        }
    }

    private void UpdateToolStatusSummaries(IReadOnlyList<ToolCapabilityResult> capabilities)
    {
        static string FormatTool(ToolCapabilityResult? cap, string name)
        {
            if (cap is null) return $"{name}: Not checked";
            return cap.Found
                ? $"{name}: {cap.Version} ({cap.ExecutablePath ?? "PATH"})"
                : $"{name}: Missing — {cap.Message}";
        }

        YtDlpStatusText = FormatTool(
            capabilities.FirstOrDefault(c => c.ToolName == ToolNames.YtDlp), "yt-dlp");
        FfmpegStatusText = FormatTool(
            capabilities.FirstOrDefault(c => c.ToolName == ToolNames.Ffmpeg), "ffmpeg");
        FfprobeStatusText = FormatTool(
            capabilities.FirstOrDefault(c => c.ToolName == ToolNames.Ffprobe), "ffprobe");
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var path = await _uiHost.PickFolderAsync("Select output directory", OutputDirectory);
        if (!string.IsNullOrWhiteSpace(path))
            OutputDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseTempAsync()
    {
        var path = await _uiHost.PickFolderAsync("Select temporary working directory", TemporaryDirectory);
        if (!string.IsNullOrWhiteSpace(path))
            TemporaryDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseYtDlpAsync()
    {
        var path = await _uiHost.PickOpenFileAsync("Select yt-dlp executable", "Executables", ["*.exe", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            YtDlpPath = path;
            await CheckDependenciesAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseFfmpegAsync()
    {
        var path = await _uiHost.PickOpenFileAsync("Select ffmpeg executable", "Executables", ["*.exe", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            FfmpegPath = path;
            await CheckDependenciesAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseFfprobeAsync()
    {
        var path = await _uiHost.PickOpenFileAsync("Select ffprobe executable", "Executables", ["*.exe", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            FfprobePath = path;
            await CheckDependenciesAsync();
        }
    }

    [RelayCommand]
    private void BeginNewProfile()
    {
        _profileEditorMode = "New";
        ProfileNameEditorPrompt = "New profile name:";
        ProfileNameInput = MakeUniqueProfileName("Custom");
        IsProfileNameEditorOpen = true;
    }

    [RelayCommand]
    private void BeginRenameProfile()
    {
        if (SelectedProfile is null ||
            string.Equals(SelectedProfile.Name, FetchForgeProfile.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("The Default profile cannot be renamed. Use Duplicate instead.");
            return;
        }

        _profileEditorMode = "Rename";
        ProfileNameEditorPrompt = $"Rename '{SelectedProfile.Name}' to:";
        ProfileNameInput = SelectedProfile.Name;
        IsProfileNameEditorOpen = true;
    }

    [RelayCommand]
    private async Task CommitProfileNameAsync()
    {
        var desired = ProfileNameInput.Trim();
        if (string.IsNullOrWhiteSpace(desired))
            return;

        if (_profileEditorMode == "New")
        {
            var unique = MakeUniqueProfileName(desired);
            var created = new FetchForgeProfile { Name = unique };
            await _profileService.SaveToStoreAsync(created);
            Profiles.Add(created);
            SelectedProfile = created;
            AppendLog($"Created profile '{created.Name}'.");
        }
        else if (_profileEditorMode == "Rename" && SelectedProfile is not null)
        {
            var oldName = SelectedProfile.Name;
            if (!string.Equals(oldName, desired, StringComparison.OrdinalIgnoreCase))
            {
                var unique = MakeUniqueProfileName(desired);
                _profileService.DeleteFromStore(oldName);
                SelectedProfile.Name = unique;
                await _profileService.SaveToStoreAsync(SelectedProfile);

                var index = Profiles.IndexOf(SelectedProfile);
                if (index >= 0)
                {
                    var current = SelectedProfile;
                    _suppressUpdates = true;
                    Profiles[index] = current;
                    SelectedProfile = current;
                    _suppressUpdates = false;
                }

                _settings.LastProfileName = unique;
                await _settingsService.SaveAsync(_settings);
                AppendLog($"Renamed profile '{oldName}' to '{unique}'.");
            }
        }

        IsProfileNameEditorOpen = false;
    }

    [RelayCommand]
    private void CancelProfileName() => IsProfileNameEditorOpen = false;

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile is null)
            return;

        var name = MakeUniqueProfileName($"{SelectedProfile.Name} Copy");
        var clone = SelectedProfile.Clone(name);
        await _profileService.SaveToStoreAsync(clone);
        Profiles.Add(clone);
        SelectedProfile = clone;
        AppendLog($"Duplicated profile as '{clone.Name}'.");
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null ||
            string.Equals(SelectedProfile.Name, FetchForgeProfile.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var toRemove = SelectedProfile;
        _profileService.DeleteFromStore(toRemove.Name);
        Profiles.Remove(toRemove);
        SelectedProfile = Profiles.FirstOrDefault();
        AppendLog($"Deleted profile '{toRemove.Name}'.");
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        var path = await _uiHost.PickOpenFileAsync(
            "Import profile", "JSON profile", ["*.json"]);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var loaded = await _profileService.LoadAsync(path);
            var existing = Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, loaded.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (string.Equals(existing.Name, FetchForgeProfile.DefaultName, StringComparison.OrdinalIgnoreCase))
                    loaded.Name = MakeUniqueProfileName("Imported");
                else
                    Profiles.Remove(existing);
            }

            await _profileService.SaveToStoreAsync(loaded);
            Profiles.Add(loaded);
            SelectedProfile = loaded;
            AppendLog($"Imported profile '{loaded.Name}'.");
        }
        catch (Exception ex)
        {
            AppendLog($"Profile import failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (SelectedProfile is null)
            return;

        var suggested = $"{SelectedProfile.Name}.json";
        var path = await _uiHost.PickSaveFileAsync(
            "Export profile", suggested, "json", "JSON profile", ["*.json"]);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            await _profileService.SaveAsync(path, SelectedProfile);
            AppendLog($"Exported profile to {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"Profile export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetProfileDefaults()
    {
        if (SelectedProfile is null)
            return;

        SelectedProfile.PreservePlaylistOrder = true;
        SelectedProfile.OverwriteExisting = false;
        SelectedProfile.YtDlp.Options.Clear();
        SelectedProfile.YtDlp.ExtraArguments.Clear();
        SelectedProfile.Ffmpeg.Enabled = true;
        SelectedProfile.Ffmpeg.MaxConcurrentJobs = FfmpegProfileSettings.DefaultConcurrentJobs;
        SelectedProfile.Ffmpeg.Options.Clear();
        SelectedProfile.Ffmpeg.ExtraArguments.Clear();

        ApplyProfileToFields(SelectedProfile);
        OnActiveProfileEdited();
        AppendLog($"Reset '{SelectedProfile.Name}' to defaults.");
    }

    [RelayCommand]
    private async Task CopyReportAsync()
    {
        var text = string.IsNullOrWhiteSpace(FinalReportText) ? LogText : FinalReportText;
        if (!string.IsNullOrWhiteSpace(text))
            await _uiHost.CopyTextAsync(text);
    }

    [RelayCommand]
    private void ClearLog() => LogText = string.Empty;

    private string MakeUniqueProfileName(string baseName)
    {
        var candidate = baseName.Trim();
        if (!Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            return candidate;

        for (var i = 2; i < 1000; i++)
        {
            var next = $"{candidate} ({i})";
            if (!Profiles.Any(p => string.Equals(p.Name, next, StringComparison.OrdinalIgnoreCase)))
                return next;
        }

        return $"{candidate} ({Guid.NewGuid():N}"[..8] + ")";
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var stamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText = string.IsNullOrEmpty(LogText)
            ? stamped
            : $"{LogText}{Environment.NewLine}{stamped}";
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void QueueSaveSettings()
    {
        var snapshot = new AppSettings
        {
            OutputDirectory = _settings.OutputDirectory,
            TemporaryDirectory = _settings.TemporaryDirectory,
            LastProfileName = _settings.LastProfileName,
            ShowAdvancedOptions = _settings.ShowAdvancedOptions,
            ToolPaths = _settings.ToolPaths.Clone()
        };

        _ = PersistAsync(() => _settingsService.SaveAsync(snapshot));
    }

    private void QueueSaveProfile(FetchForgeProfile profile)
    {
        var snapshot = profile.Clone();
        _ = PersistAsync(() => _profileService.SaveToStoreAsync(snapshot));
    }

    private async Task PersistAsync(Func<Task> action)
    {
        await _saveGate.WaitAsync();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            RunOnUi(() => AppendLog($"Warning: failed to save configuration: {ex.Message}"));
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
