using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FetchForge.Core.Models;

namespace FetchForge.ViewModels;

public sealed partial class OptionItemViewModel : ObservableObject
{
    private static readonly IBrush SupportedBg = new SolidColorBrush(Color.Parse("#193829"));
    private static readonly IBrush SupportedFg = new SolidColorBrush(Color.Parse("#6EE7B7"));
    private static readonly IBrush UnsupportedBg = new SolidColorBrush(Color.Parse("#451E23"));
    private static readonly IBrush UnsupportedFg = new SolidColorBrush(Color.Parse("#FCA5A5"));
    private static readonly IBrush UnverifiedBg = new SolidColorBrush(Color.Parse("#3D3018"));
    private static readonly IBrush UnverifiedFg = new SolidColorBrush(Color.Parse("#FCD34D"));
    private static readonly IBrush NeutralBg = new SolidColorBrush(Color.Parse("#252B36"));
    private static readonly IBrush NeutralFg = new SolidColorBrush(Color.Parse("#9CA3B2"));

    private readonly Action _onChanged;
    private FetchForgeProfile _profile;
    private bool _suppressNotify;

    public OptionItemViewModel(
        ToolOptionDefinition definition,
        FetchForgeProfile profile,
        Action onChanged)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        NumericMinimum = definition.Minimum.HasValue
            ? (decimal)definition.Minimum.Value
            : -1_000_000m;
        NumericMaximum = definition.Maximum.HasValue
            ? (decimal)definition.Maximum.Value
            : 1_000_000m;
        NumericIncrement = IsInteger ? 1m : 0.5m;
        NumericFormat = IsInteger ? "0" : "0.##";

        RefreshFromProfile(profile);
    }

    public ToolOptionDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Description => Definition.Description;
    public string Category => Definition.Category;
    public string CommandLineSwitch => Definition.CommandLineArgument ?? "App option";
    public bool IsAdvanced => Definition.IsAdvanced;
    public string Placeholder => Definition.Placeholder ?? string.Empty;
    public IReadOnlyList<string> AllowedValues => Definition.AllowedValues;

    public bool IsBoolean => Definition.ValueType == ToolOptionValueType.Boolean;
    public bool IsInteger => Definition.ValueType == ToolOptionValueType.Integer;
    public bool IsDecimal => Definition.ValueType == ToolOptionValueType.Decimal;
    public bool IsNumeric => IsInteger || IsDecimal;
    public bool IsEnumeration => Definition.ValueType == ToolOptionValueType.Enumeration;
    public bool IsMultilineText =>
        Definition.Id == "ffmpeg.filter-chain" ||
        Definition.ValueType == ToolOptionValueType.StringList;
    public bool IsSingleLineText =>
        (Definition.ValueType == ToolOptionValueType.String ||
         Definition.ValueType == ToolOptionValueType.StringList) &&
        !IsMultilineText;

    public decimal NumericMinimum { get; }
    public decimal NumericMaximum { get; }
    public decimal NumericIncrement { get; }
    public string NumericFormat { get; }

    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private decimal? _numericValue;
    [ObservableProperty] private string _stringValue = string.Empty;
    [ObservableProperty] private bool _isOverridden;
    [ObservableProperty] private bool _isApplicable = true;
    [ObservableProperty] private string _inapplicableReason = string.Empty;
    [ObservableProperty] private bool _isVisible = true;

    [ObservableProperty] private ToolOptionSupportStatus _supportStatus = ToolOptionSupportStatus.Unverified;
    [ObservableProperty] private string _badgeText = "Checking...";
    [ObservableProperty] private string _supportMessage = "Checking installed tool...";
    [ObservableProperty] private IBrush _badgeBackground = NeutralBg;
    [ObservableProperty] private IBrush _badgeForeground = NeutralFg;

    partial void OnBoolValueChanged(bool value)
    {
        if (_suppressNotify || !IsBoolean)
            return;

        _profile.SetValue(Definition, value ? "true" : "false");
        IsOverridden = _profile.IsOverridden(Definition);
        _onChanged();
    }

    partial void OnNumericValueChanged(decimal? value)
    {
        if (_suppressNotify || !IsNumeric)
            return;

        var formatted = value.HasValue
            ? (IsInteger
                ? ((long)Math.Round(value.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : string.Empty;

        _profile.SetValue(Definition, formatted);
        IsOverridden = _profile.IsOverridden(Definition);
        _onChanged();
    }

    partial void OnStringValueChanged(string value)
    {
        if (_suppressNotify || (!IsSingleLineText && !IsMultilineText && !IsEnumeration))
            return;

        _profile.SetValue(Definition, value);
        IsOverridden = _profile.IsOverridden(Definition);
        _onChanged();
    }

    public void RefreshFromProfile(FetchForgeProfile profile)
    {
        _profile = profile;
        _suppressNotify = true;

        try
        {
            var raw = profile.GetValue(Definition) ?? string.Empty;

            if (IsBoolean)
            {
                BoolValue = OptionValueFormatter.TryParseBoolean(raw, out var b) && b;
            }
            else if (IsInteger)
            {
                NumericValue = OptionValueFormatter.TryParseInteger(raw, out var n)
                    ? n
                    : null;
            }
            else if (IsDecimal)
            {
                NumericValue = OptionValueFormatter.TryParseDecimal(raw, out var d)
                    ? (decimal)d
                    : null;
            }
            else
            {
                StringValue = raw;
            }

            IsOverridden = profile.IsOverridden(Definition);

            if (Definition.AppliesWhen == OptionApplicability.WhenProcessingDisabled &&
                profile.Ffmpeg.Enabled)
            {
                IsApplicable = false;
                InapplicableReason =
                    "Inactive while FFmpeg audio processing is enabled (prevents double encoding).";
            }
            else
            {
                IsApplicable = true;
                InapplicableReason = string.Empty;
            }
        }
        finally
        {
            _suppressNotify = false;
        }
    }

    public void UpdateCapability(ToolOptionCapabilityMatch match)
    {
        SupportStatus = match.Status;
        SupportMessage = match.Message;

        switch (match.Status)
        {
            case ToolOptionSupportStatus.Supported:
                BadgeText = "Supported";
                BadgeBackground = SupportedBg;
                BadgeForeground = SupportedFg;
                break;
            case ToolOptionSupportStatus.Unsupported:
                BadgeText = "Unsupported";
                BadgeBackground = UnsupportedBg;
                BadgeForeground = UnsupportedFg;
                break;
            case ToolOptionSupportStatus.NotApplicable:
                BadgeText = "Built-in";
                BadgeBackground = NeutralBg;
                BadgeForeground = NeutralFg;
                break;
            default:
                BadgeText = "Unverified";
                BadgeBackground = UnverifiedBg;
                BadgeForeground = UnverifiedFg;
                break;
        }
    }

    public bool MatchesFilter(bool showAdvanced, string searchText)
    {
        if (!showAdvanced && IsAdvanced)
            return false;

        if (string.IsNullOrWhiteSpace(searchText))
            return true;

        var q = searchText.Trim();
        return DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               CommandLineSwitch.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Reset()
    {
        _profile.ResetValue(Definition);
        RefreshFromProfile(_profile);
        _onChanged();
    }
}
