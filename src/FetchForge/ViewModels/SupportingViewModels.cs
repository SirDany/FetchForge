using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FetchForge.Core.Models;

namespace FetchForge.ViewModels;

public sealed partial class OptionCategoryGroupViewModel : ObservableObject
{
    public OptionCategoryGroupViewModel(
        string category,
        IEnumerable<OptionItemViewModel> options)
    {
        Category = category;
        Options = new ObservableCollection<OptionItemViewModel>(options);
        HasVisibleOptions = Options.Count > 0;
    }

    public string Category { get; }
    public ObservableCollection<OptionItemViewModel> Options { get; }

    [ObservableProperty] private bool _hasVisibleOptions;

    public void ApplyFilter(bool showAdvanced, string searchText)
    {
        var anyVisible = false;
        foreach (var option in Options)
        {
            var visible = option.MatchesFilter(showAdvanced, searchText);
            option.IsVisible = visible;
            if (visible)
                anyVisible = true;
        }

        HasVisibleOptions = anyVisible;
    }
}

public sealed class HelpOptionItemViewModel
{
    private readonly Action<HelpOptionInfo> _onAdd;

    public HelpOptionItemViewModel(
        HelpOptionInfo info,
        Action<HelpOptionInfo> onAdd)
    {
        Info = info;
        _onAdd = onAdd;
        AddCommand = new RelayCommand(() => _onAdd(Info));
    }

    public HelpOptionInfo Info { get; }
    public string Section => Info.Section;
    public string Description => Info.Description;

    public string SwitchSummary
    {
        get
        {
            var switches = Info.Aliases.Count > 0
                ? $"{Info.Switch} ({string.Join(", ", Info.Aliases)})"
                : Info.Switch;
            return Info.ValuePlaceholder is not null
                ? $"{switches} <{Info.ValuePlaceholder}>"
                : switches;
        }
    }

    public IRelayCommand AddCommand { get; }
}

public sealed partial class FileProgressItemViewModel : ObservableObject
{
    public FileProgressItemViewModel(JobFileItem item)
    {
        Id = item.Id;
        UpdateFrom(item);
    }

    public string Id { get; }
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private FileItemStatus _status;
    [ObservableProperty] private string _statusBadge = string.Empty;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public void UpdateFrom(JobFileItem item)
    {
        Title = string.IsNullOrWhiteSpace(item.Title) ? item.Id : item.Title;
        Status = item.Status;
        StatusBadge = item.Status.ToString();
        ProgressPercent = item.ProgressPercent;
        StatusText = string.IsNullOrWhiteSpace(item.StatusText)
            ? item.Status.ToString()
            : item.StatusText;
        OutputPath = item.OutputPath ?? item.SourcePath;
        ErrorMessage = item.ErrorMessage;
    }
}

public sealed class ValidationIssueItemViewModel
{
    private static readonly IBrush ErrorBg = new SolidColorBrush(Color.Parse("#451E23"));
    private static readonly IBrush ErrorFg = new SolidColorBrush(Color.Parse("#FCA5A5"));
    private static readonly IBrush WarningBg = new SolidColorBrush(Color.Parse("#3D3018"));
    private static readonly IBrush WarningFg = new SolidColorBrush(Color.Parse("#FCD34D"));

    public ValidationIssueItemViewModel(OptionValidationIssue issue)
    {
        Issue = issue;
        IsError = issue.Severity == OptionValidationSeverity.Error;
        SeverityText = IsError ? "ERROR" : "WARN";
        Tool = issue.Tool;
        Message = issue.Message;
        BadgeBackground = IsError ? ErrorBg : WarningBg;
        BadgeForeground = IsError ? ErrorFg : WarningFg;
    }

    public OptionValidationIssue Issue { get; }
    public bool IsError { get; }
    public string SeverityText { get; }
    public string Tool { get; }
    public string Message { get; }
    public IBrush BadgeBackground { get; }
    public IBrush BadgeForeground { get; }
}
