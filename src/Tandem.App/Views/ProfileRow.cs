using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Tandem.Core.Profiles;

namespace Tandem.App.Views;

/// <summary>One account in the Manage window.</summary>
public sealed class ProfileRow : INotifyPropertyChanged
{
    private Profile _profile;
    private Services.AccountRunState _runState;
    private bool _isCurrent;
    private DateTimeOffset? _lastUsed;

    public ProfileRow(Profile profile) => _profile = profile;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Profile Profile
    {
        get => _profile;
        set
        {
            _profile = value;
            OnChanged(string.Empty);
        }
    }

    public string Name => _profile.DisplayName;

    /// <summary>First letter of the name, for the avatar.</summary>
    public string Initial => InitialOf(_profile.DisplayName);

    public Brush ColorBrush => (Brush)new BrushConverter().ConvertFromString(_profile.Color)!;

    public bool IsDefault => _profile.IsDefault;

    public Visibility DefaultBadgeVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CurrentBadgeVisibility => _isCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RunningVisibility => _runState == Services.AccountRunState.NotRunning ? Visibility.Collapsed : Visibility.Visible;

    public string StatusText => StatusOf(_runState);

    public static string StatusOf(Services.AccountRunState state) => state switch
    {
        Services.AccountRunState.Open => "Open",
        Services.AccountRunState.Background => "In background",
        _ => "Not open",
    };

    public string PlanText => PlanName(_profile.Plan);

    public Visibility PlanVisibility => _profile.Plan == AccountPlan.Unknown ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>"Active now", "Used 5 min ago", "Not used yet".</summary>
    public string LastUsedText =>
        _isCurrent && _runState == Services.AccountRunState.Open ? "Active now"
        : _lastUsed is { } at ? "Used " + Ago(at)
        : "Not used yet";

    public DateTimeOffset? LastUsed
    {
        get => _lastUsed;
        set
        {
            if (_lastUsed != value)
            {
                _lastUsed = value;
                OnChanged(nameof(LastUsedText));
            }
        }
    }

    public static string PlanName(AccountPlan plan) => plan switch
    {
        AccountPlan.Free => "Free",
        AccountPlan.Pro => "Pro",
        AccountPlan.Max => "Max",
        AccountPlan.Team => "Team",
        AccountPlan.Enterprise => "Enterprise",
        _ => string.Empty,
    };

    public static string Ago(DateTimeOffset at)
    {
        var span = DateTimeOffset.UtcNow - at;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours} h ago"
            : span.TotalDays < 2 ? "yesterday"
            : span.TotalDays < 30 ? $"{(int)span.TotalDays} days ago"
            : at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    public string ActionLabel => _runState switch
    {
        Services.AccountRunState.Open => _isCurrent ? "Show" : "Switch",
        Services.AccountRunState.Background => "Show",
        _ => "Open",
    };

    public LimitView? UsageFiveHour { get; private set; }

    public LimitView? UsageWeekly { get; private set; }

    public string? UsageLimitText { get; private set; }

    public Visibility UsageVisibility => UsageFiveHour is null && UsageWeekly is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility UsageLimitVisibility => UsageLimitText is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Takes the account's latest usage; the card only refreshes when it changed.</summary>
    public void SetUsage(Tandem.Core.Usage.UsageReport? report, DateTimeOffset now)
    {
        var fiveHour = LimitView.FiveHour(report, now);
        var weekly = LimitView.Weekly(report, now);
        var limit = report is null ? null : UsageText.LimitLine(report, now);
        if (fiveHour == UsageFiveHour && weekly == UsageWeekly && limit == UsageLimitText)
        {
            return;
        }

        UsageFiveHour = fiveHour;
        UsageWeekly = weekly;
        UsageLimitText = limit;
        OnChanged(nameof(UsageFiveHour));
        OnChanged(nameof(UsageWeekly));
        OnChanged(nameof(UsageLimitText));
        OnChanged(nameof(UsageVisibility));
        OnChanged(nameof(UsageLimitVisibility));
    }

    public string FolderText => _profile.IsDefault
        ? "Claude's own data folder"
        : Shorten(_profile.DataFolder ?? string.Empty);

    public Services.AccountRunState RunState
    {
        get => _runState;
        set
        {
            if (_runState != value)
            {
                _runState = value;
                OnChanged(string.Empty);
            }
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent != value)
            {
                _isCurrent = value;
                OnChanged(string.Empty);
            }
        }
    }

    /// <summary>Folder prefixes shown as variables: shorter, and no user name on screen.</summary>
    internal static List<(string Prefix, string Alias)> PathAliases { get; } =
    [
        (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
        (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
        (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
    ];

    public static string InitialOf(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "?";
        }

        var first = StringInfo.GetNextTextElement(trimmed);
        return first.ToUpper(CultureInfo.CurrentCulture);
    }

    private static string Shorten(string path)
    {
        foreach (var (prefix, alias) in PathAliases)
        {
            if (prefix.Length > 0 && path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return alias + path[prefix.Length..];
            }
        }

        return path;
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
