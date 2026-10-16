using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Tandem.Core.Profiles;
using Tandem.Core.Usage;

namespace Tandem.App.Views;

/// <summary>How full a limit is; picks the meter's color.</summary>
public enum UsageLevel
{
    Low,
    Medium,
    High,
    Full,
}

/// <summary>One limit, ready to show: a meter value, its percentage and a line about the reset.</summary>
public sealed record LimitView(string Title, string ShortTitle, double Value, string PercentText, string Detail, UsageLevel Level)
{
    public string Compact => $"{ShortTitle} {PercentText}";

    public static LimitView? FiveHour(UsageReport? report, DateTimeOffset now) =>
        UsageText.AsOf(report?.FiveHour, now) is { } s
            ? new("5-hour limit", "5-hour", s.Percent, $"{s.Percent}%", UsageText.FiveHourDetail(s, now), UsageText.LevelOf(s.Percent))
            : null;

    public static LimitView? Weekly(UsageReport? report, DateTimeOffset now) =>
        UsageText.AsOf(report?.Weekly, now) is { } s
            ? new("Weekly limit", "Weekly", s.Percent, $"{s.Percent}%", UsageText.WeeklyDetail(s, now), UsageText.LevelOf(s.Percent))
            : null;
}

/// <summary>Wording for usage: reset times, "in 2 h 5 min", what is left.</summary>
internal static class UsageText
{
    /// <summary>A reset time known to within this is shown as a countdown.</summary>
    private static readonly TimeSpan Precise = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The status at <paramref name="now"/>: readings are a little older than the screen, so a
    /// reset time that has just passed already counts as a reset.
    /// </summary>
    public static LimitStatus? AsOf(LimitStatus? status, DateTimeOffset now) =>
        status is { Reset: false, ResetsAt: { } at } && at <= now
            ? status with { Percent = 0, Reset = status.LastSeen > 0, ResetsAt = null, Uncertainty = TimeSpan.Zero }
            : status;

    public static UsageLevel LevelOf(int percent) => percent switch
    {
        >= 100 => UsageLevel.Full,
        >= 90 => UsageLevel.High,
        >= 60 => UsageLevel.Medium,
        _ => UsageLevel.Low,
    };

    public static string FiveHourDetail(LimitStatus status, DateTimeOffset now)
    {
        if (status.Reset)
        {
            return "Window ended since the last reading · full limit back";
        }

        if (status.Percent == 0)
        {
            return "Not in use · full limit available";
        }

        if (status.ResetsAt is not { } at)
        {
            return "Reset time not known";
        }

        return status.Uncertainty <= Precise
            ? $"Resets in {In(at - now)} · {When(at, now)}"
            : $"Resets by {When(at, now)} at the latest";
    }

    public static string WeeklyDetail(LimitStatus status, DateTimeOffset now)
    {
        if (status.Reset)
        {
            return "New week since the last reading · full limit back";
        }

        if (status.ResetsAt is not { } at)
        {
            return status.Percent == 0 ? "Not used this week" : "Reset day shows after the first weekly reset";
        }

        var span = at - now;
        if (status.Uncertainty <= TimeSpan.FromHours(1))
        {
            return span < TimeSpan.FromDays(1) ? $"Resets in {In(span)} · {When(at, now)}" : $"Resets {When(at, now)}";
        }

        return span < TimeSpan.FromDays(1) ? $"Resets by {When(at, now)} · in {In(span)} at most" : $"Resets by {When(at, now)}";
    }

    /// <summary>"10:29 PM", "tomorrow 9:00 AM", "Thu 4:15 PM", "24 Oct 4:15 PM".</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        if (local.Date == today)
        {
            return time;
        }

        if (local.Date == today.AddDays(1))
        {
            return "tomorrow " + time;
        }

        return local.Date < today.AddDays(7)
            ? local.ToString("ddd", CultureInfo.CurrentCulture) + " " + time
            : local.ToString("d MMM", CultureInfo.CurrentCulture) + " " + time;
    }

    /// <summary>"under a minute", "25 min", "3 h 5 min", "2 days 4 h".</summary>
    public static string In(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return "under a minute";
        }

        // Round up: "resets in 1 min" should never be shown after it has already reset.
        var minutes = (long)Math.Ceiling(span.TotalMinutes);
        if (minutes < 60)
        {
            return $"{minutes} min";
        }

        if (minutes < 24 * 60)
        {
            return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
        }

        var days = minutes / (24 * 60);
        var hours = minutes % (24 * 60) / 60;
        return hours == 0 ? $"{days} {(days == 1 ? "day" : "days")}" : $"{days} {(days == 1 ? "day" : "days")} {hours} h";
    }

    /// <summary>"55% of the 5-hour limit and 49% of the weekly limit left."</summary>
    public static string Left(UsageReport report)
    {
        var parts = new List<string>();
        if (report.FiveHour is { } fiveHour)
        {
            parts.Add($"{fiveHour.Left}% of the 5-hour limit");
        }

        if (report.Weekly is { } weekly)
        {
            parts.Add($"{weekly.Left}% of the weekly limit");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" and ", parts) + " left.";
    }

    /// <summary>Which limit is reached and when it is back, e.g. "5-hour limit reached · back by 8:10 PM".</summary>
    public static string? LimitLine(UsageReport report, DateTimeOffset now)
    {
        var current = Current(report, now);
        if (!current.LimitReached)
        {
            return null;
        }

        var which = current.FiveHour?.Reached == true && current.Weekly?.Reached == true ? "Both limits"
            : current.Weekly?.Reached == true ? "Weekly limit"
            : "5-hour limit";
        return current.AvailableAt is { } at ? $"{which} reached · back by {When(at, now)}" : $"{which} reached";
    }

    /// <summary>The short form for the switcher: "At limit · back 8:10 PM".</summary>
    public static string? ShortLimitLine(UsageReport report, DateTimeOffset now)
    {
        var current = Current(report, now);
        return !current.LimitReached ? null
            : current.AvailableAt is { } at ? $"At limit · back {When(at, now)}"
            : "At limit";
    }

    private static UsageReport Current(UsageReport report, DateTimeOffset now) =>
        report with { FiveHour = AsOf(report.FiveHour, now), Weekly = AsOf(report.Weekly, now) };

    /// <summary>"Last reading 4 min ago", or a nudge to open the account when it is old.</summary>
    public static string Updated(UsageReport? report, Services.AccountRunState state)
    {
        if (report is null)
        {
            return state == Services.AccountRunState.NotRunning
                ? "No readings yet · open this account to start"
                : "No readings yet · the first one comes within 15 minutes";
        }

        var text = "Last reading " + ProfileRow.Ago(report.UpdatedAt);
        return state == Services.AccountRunState.NotRunning && DateTimeOffset.UtcNow - report.UpdatedAt > TimeSpan.FromHours(1)
            ? text + " · open it to update"
            : text;
    }
}

/// <summary>One account on the Usage page.</summary>
public sealed class UsageRow : INotifyPropertyChanged
{
    public UsageRow(Profile profile) => Profile = profile;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Profile Profile { get; }

    public string Name => Profile.DisplayName;

    public string Initial => ProfileRow.InitialOf(Profile.DisplayName);

    public Brush ColorBrush => (Brush)new BrushConverter().ConvertFromString(Profile.Color)!;

    public string PlanText => ProfileRow.PlanName(Profile.Plan);

    public Visibility PlanVisibility => Profile.Plan == AccountPlan.Unknown ? Visibility.Collapsed : Visibility.Visible;

    public Services.AccountRunState RunState { get; private set; }

    public bool IsCurrent { get; private set; }

    public Visibility CurrentBadgeVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public string StateText => ProfileRow.StatusOf(RunState);

    public string UpdatedText { get; private set; } = string.Empty;

    public LimitView? FiveHour { get; private set; }

    public LimitView? Weekly { get; private set; }

    public IReadOnlyList<UsageSample>? History { get; private set; }

    public Visibility DataVisibility => History is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NoDataVisibility => History is null ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBest { get; private set; }

    public Visibility BestVisibility => IsBest ? Visibility.Visible : Visibility.Collapsed;

    public string? LimitText { get; private set; }

    public Visibility LimitVisibility => LimitText is null ? Visibility.Collapsed : Visibility.Visible;

    public string? SharedText { get; private set; }

    public Visibility SharedVisibility => SharedText is null ? Visibility.Collapsed : Visibility.Visible;

    public string ActionLabel => RunState switch
    {
        Services.AccountRunState.Open => IsCurrent ? "Show" : "Switch",
        Services.AccountRunState.Background => "Show",
        _ => "Open",
    };

    /// <summary>Takes the latest numbers; the view only refreshes when something it shows changed.</summary>
    public void Update(UsageReport? report, Services.AccountRunState state, bool isCurrent, bool isBest, string? sharedWith, DateTimeOffset now)
    {
        var fiveHour = LimitView.FiveHour(report, now);
        var weekly = LimitView.Weekly(report, now);
        var updated = UsageText.Updated(report, state);
        var limit = report is null ? null : UsageText.LimitLine(report, now);
        var shared = sharedWith is null ? null : $"Signed in to the same Claude account as {sharedWith}, so they share these limits.";
        var history = report?.History;
        if (state == RunState && isCurrent == IsCurrent && isBest == IsBest && fiveHour == FiveHour && weekly == Weekly
            && updated == UpdatedText && limit == LimitText && shared == SharedText && ReferenceEquals(history, History))
        {
            return;
        }

        RunState = state;
        IsCurrent = isCurrent;
        IsBest = isBest;
        // Equal values keep their old instance, so their part of the view is not rebuilt.
        FiveHour = fiveHour == FiveHour ? FiveHour : fiveHour;
        Weekly = weekly == Weekly ? Weekly : weekly;
        History = history;
        UpdatedText = updated;
        LimitText = limit;
        SharedText = shared;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
