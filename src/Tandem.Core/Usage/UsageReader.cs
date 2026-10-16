using System.Text.Json;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;

namespace Tandem.Core.Usage;

/// <summary>
/// One usage sample the Claude app saved: percent used of the 5-hour limit and of the weekly
/// limit (either may be missing), and an opaque organization key that is only compared, never shown.
/// </summary>
public sealed record UsageSample(DateTimeOffset At, int? FiveHour, int? Weekly, string? Org);

/// <summary>Where one limit stands now.</summary>
/// <param name="Percent">Percent used now: 0 when the window has ended since the last sample.</param>
/// <param name="LastSeen">Percent in the latest sample.</param>
/// <param name="Reset">The window ended after the latest sample, so the full limit is back.</param>
/// <param name="ResetsAt">When the current window ends, at the latest; null when unknown.</param>
/// <param name="Uncertainty">How much earlier than <paramref name="ResetsAt"/> it may end.</param>
public sealed record LimitStatus(int Percent, int LastSeen, bool Reset, DateTimeOffset? ResetsAt, TimeSpan Uncertainty)
{
    public bool Reached => Percent >= 100;

    public int Left => 100 - Percent;
}

/// <summary>An account's usage now, worked out from the samples of the account it is signed in to.</summary>
public sealed record UsageReport(DateTimeOffset UpdatedAt, LimitStatus? FiveHour, LimitStatus? Weekly, IReadOnlyList<UsageSample> History)
{
    /// <summary>What is left of the tighter limit, 0–100.</summary>
    public int Headroom => 100 - Math.Max(FiveHour?.Percent ?? 0, Weekly?.Percent ?? 0);

    public bool LimitReached => FiveHour?.Reached == true || Weekly?.Reached == true;

    /// <summary>When every reached limit is back, at the latest; null when not limited or unknown.</summary>
    public DateTimeOffset? AvailableAt
    {
        get
        {
            var reached = new[] { FiveHour, Weekly }.Where(l => l?.Reached == true).ToList();
            return reached.Count == 0 || reached.Any(l => l!.ResetsAt is null) ? null : reached.Max(l => l!.ResetsAt);
        }
    }
}

/// <summary>Usage of every account, plus accounts that turned out to be signed in to the same Claude account.</summary>
public sealed record UsageOverview(IReadOnlyDictionary<string, UsageReport> Reports, IReadOnlyList<IReadOnlyList<string>> SameAccount)
{
    public static readonly UsageOverview Empty = new(new Dictionary<string, UsageReport>(), []);

    public UsageReport? For(string profileId) => Reports.GetValueOrDefault(profileId);
}

/// <summary>The account to use now. <see cref="AllLimited"/> means every account with data is at a limit.</summary>
public sealed record UsageRecommendation(string ProfileId, bool AllLimited, DateTimeOffset? AvailableAt);

/// <summary>
/// Reads the usage history the Claude app keeps in each data folder
/// (<c>plan-usage-history.json</c>: a sample about every 15 minutes while the app runs, ~30 days).
/// It holds usage percentages, times and an organization key, nothing about sign-in. The file is
/// opened read-only and its values are never logged or sent anywhere.
/// </summary>
public static class UsageReader
{
    public const string FileName = "plan-usage-history.json";
    public static readonly TimeSpan FiveHourWindow = TimeSpan.FromHours(5);
    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>A fall of more than this many points between two samples is a weekly reset.</summary>
    private const int WeeklyDropPoints = 10;
    private const long MaxFileBytes = 16 * 1024 * 1024;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> WarnedProfiles = new(StringComparer.Ordinal);

    /// <summary>
    /// The file for a profile (the newest one when several places have it), or null when Claude
    /// has not written one yet.
    /// </summary>
    public static string? Locate(Profile profile) =>
        ProfileSetup.SourceRoots(profile)
            .Select(root => new FileInfo(Path.Combine(root, FileName)))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();

    public static UsageOverview ReadAll(IEnumerable<Profile> profiles, DateTimeOffset now) =>
        Combine(profiles.Select(p => (p.Id, ReadSamples(p))).ToList(), now);

    /// <summary>
    /// Builds the overview. Usage belongs to the Claude account (organization), not the folder, so the
    /// samples of one organization are merged from every folder that has them; each profile shows
    /// the organization it was signed in to last.
    /// </summary>
    public static UsageOverview Combine(IReadOnlyList<(string ProfileId, IReadOnlyList<UsageSample> Samples)> perProfile, DateTimeOffset now)
    {
        var byOrg = new Dictionary<string, List<UsageSample>>(StringComparer.Ordinal);
        var currentOrg = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (profileId, samples) in perProfile)
        {
            if (samples.Count == 0)
            {
                continue;
            }

            // Samples without an organization key stay with their own folder.
            string Key(UsageSample s) => s.Org is { Length: > 0 } org ? "org:" + org : "folder:" + profileId;
            currentOrg[profileId] = Key(samples.MaxBy(s => s.At)!);
            foreach (var group in samples.GroupBy(Key))
            {
                if (!byOrg.TryGetValue(group.Key, out var list))
                {
                    byOrg[group.Key] = list = [];
                }

                list.AddRange(group);
            }
        }

        var reports = new Dictionary<string, UsageReport>(StringComparer.Ordinal);
        var analyzed = new Dictionary<string, UsageReport?>(StringComparer.Ordinal);
        foreach (var (profileId, org) in currentOrg)
        {
            if (!analyzed.TryGetValue(org, out var report))
            {
                // Two folders open on the same account write samples at nearly the same moments.
                var merged = byOrg[org].OrderBy(s => s.At).ToList();
                var distinct = merged.Where((s, i) => i == 0 || (s.At - merged[i - 1].At).Duration() > TimeSpan.FromSeconds(30)).ToList();
                analyzed[org] = report = Analyze(distinct, now);
            }

            if (report is not null)
            {
                reports[profileId] = report;
            }
        }

        var same = currentOrg
            .Where(pair => pair.Value.StartsWith("org:", StringComparison.Ordinal))
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<string>)g.ToList())
            .ToList();
        return new UsageOverview(reports, same);
    }

    /// <summary>Samples from a profile's file; empty when there is none or it cannot be read.</summary>
    public static IReadOnlyList<UsageSample> ReadSamples(Profile profile)
    {
        var path = Locate(profile);
        if (path is null)
        {
            return [];
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length is 0 or > MaxFileBytes)
            {
                return [];
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return ParseSamples(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Claude may be rewriting the file; the next refresh reads it again. Logged once per
            // account so a refresh every few seconds cannot flood the log.
            if (WarnedProfiles.TryAdd(profile.Id, 0))
            {
                Log.Warn($"Could not read the usage of profile {profile.Id}: {ex.GetType().Name}.");
            }

            return [];
        }
    }

    public static IReadOnlyList<UsageSample> ParseSamples(JsonElement root)
    {
        var result = new List<UsageSample>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var sample in samples.EnumerateArray())
        {
            if (sample.ValueKind != JsonValueKind.Object
                || !sample.TryGetProperty("t", out var t) || !t.TryGetInt64(out var stamp) || stamp <= 0
                || !sample.TryGetProperty("u", out var u) || u.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var fiveHour = Percent(u, "fh");
            var weekly = Percent(u, "sd");
            if (fiveHour is null && weekly is null)
            {
                continue;
            }

            // Milliseconds since 1970 today; accept seconds too.
            DateTimeOffset at;
            try
            {
                at = stamp > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(stamp) : DateTimeOffset.FromUnixTimeSeconds(stamp);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var org = sample.TryGetProperty("org", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
            result.Add(new UsageSample(at, fiveHour, weekly, org));
        }

        return result;
    }

    /// <summary>Works out where both limits stand now from one account's samples.</summary>
    public static UsageReport? Analyze(IReadOnlyList<UsageSample> samples, DateTimeOffset now)
    {
        // Samples from the future (a clock change) would hide everything after them.
        var history = samples.Where(s => s.At <= now + TimeSpan.FromMinutes(5)).OrderBy(s => s.At).ToList();
        if (history.Count == 0)
        {
            return null;
        }

        var fiveHour = FiveHourStatus(history.Where(s => s.FiveHour is not null).Select(s => (s.At, s.FiveHour!.Value)).ToList(), now);
        var weekly = WeeklyStatus(history.Where(s => s.Weekly is not null).Select(s => (s.At, s.Weekly!.Value)).ToList(), now);
        return new UsageReport(history[^1].At, fiveHour, weekly, history.Where(s => now - s.At <= WeeklyWindow).ToList());
    }

    /// <summary>
    /// The account with the most room left. Accounts at a limit only come first when all are,
    /// then the one that frees up first wins.
    /// </summary>
    public static UsageRecommendation? Recommend(UsageOverview overview, IEnumerable<string> profileIds)
    {
        var candidates = profileIds
            .Select(id => (Id: id, Report: overview.For(id)))
            .Where(c => c.Report is not null)
            .Select(c => (c.Id, Report: c.Report!))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var free = candidates.Where(c => !c.Report.LimitReached).ToList();
        if (free.Count > 0)
        {
            var best = free
                .OrderByDescending(c => c.Report.Headroom)
                .ThenBy(c => c.Report.Weekly?.Percent ?? 0)
                .ThenBy(c => c.Report.FiveHour?.Percent ?? 0)
                .First();
            return new UsageRecommendation(best.Id, false, null);
        }

        var soonest = candidates
            .OrderBy(c => c.Report.AvailableAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(c => c.Report.Headroom)
            .First();
        return new UsageRecommendation(soonest.Id, true, soonest.Report.AvailableAt);
    }

    /// <summary>
    /// A 5-hour window starts with the first message after the previous one ended and lasts five
    /// hours. Walking back from the latest sample while use only grew finds the first sample of the
    /// current window; it began after the sample before it and no later than that one.
    /// </summary>
    internal static LimitStatus? FiveHourStatus(List<(DateTimeOffset At, int Value)> series, DateTimeOffset now)
    {
        if (series.Count == 0)
        {
            return null;
        }

        var latest = series[^1];
        if (latest.Value == 0)
        {
            return new LimitStatus(0, 0, false, null, TimeSpan.Zero);
        }

        var i = series.Count - 1;
        while (i > 0
               && series[i - 1].Value > 0
               && series[i - 1].Value <= series[i].Value
               && series[i].At - series[i - 1].At < FiveHourWindow)
        {
            i--;
        }

        DateTimeOffset resetsAt;
        TimeSpan uncertainty;
        if (latest.At - series[i].At <= FiveHourWindow)
        {
            resetsAt = series[i].At + FiveHourWindow;
            uncertainty = i > 0 ? Min(series[i].At - series[i - 1].At, FiveHourWindow) : FiveHourWindow;
        }
        else
        {
            // The run is longer than one window, so a reset happened unseen; the window holding the
            // latest sample began no later than that sample.
            resetsAt = latest.At + FiveHourWindow;
            uncertainty = FiveHourWindow;
        }

        return resetsAt <= now
            ? new LimitStatus(0, latest.Value, true, null, TimeSpan.Zero)
            : new LimitStatus(latest.Value, latest.Value, false, resetsAt, uncertainty);
    }

    /// <summary>
    /// The weekly limit resets at the same time every week. Each visible reset (a clear fall between
    /// two samples) says the reset moment lies between those samples; overlapping those intervals
    /// across weeks narrows it down. Without a visible reset the time is unknown.
    /// </summary>
    internal static LimitStatus? WeeklyStatus(List<(DateTimeOffset At, int Value)> series, DateTimeOffset now)
    {
        if (series.Count == 0)
        {
            return null;
        }

        var latest = series[^1];
        var drops = new List<(DateTimeOffset Before, DateTimeOffset After)>();
        for (var i = 1; i < series.Count; i++)
        {
            if (series[i].Value + WeeklyDropPoints < series[i - 1].Value && series[i].At - series[i - 1].At < WeeklyWindow)
            {
                drops.Add((series[i - 1].At, series[i].At));
            }
        }

        DateTimeOffset? resetsAt = null;
        var uncertainty = WeeklyWindow;
        if (WeeklyAnchor(drops) is { } anchor)
        {
            // The first reset that can still be ahead of the latest sample, skipping one that the
            // samples already show happened.
            var k = (long)Math.Floor((latest.At - anchor.Latest).Ticks / (double)WeeklyWindow.Ticks) + 1;
            var hi = anchor.Latest + (WeeklyWindow * k);
            var lo = hi - anchor.Width;
            if (lo < latest.At && drops.Any(d => d.After > lo && d.After <= latest.At))
            {
                hi += WeeklyWindow;
            }

            resetsAt = hi;
            uncertainty = anchor.Width;
        }

        var resetBy = resetsAt ?? latest.At + WeeklyWindow;
        if (resetBy <= now)
        {
            return new LimitStatus(0, latest.Value, latest.Value > 0, null, TimeSpan.Zero);
        }

        return latest.Value == 0
            ? new LimitStatus(0, 0, false, resetsAt, uncertainty)
            : new LimitStatus(latest.Value, latest.Value, false, resetsAt, uncertainty);
    }

    /// <summary>
    /// The weekly reset moment, as one past occurrence (latest possible time) and how much earlier it
    /// may be. Uses the time of week most resets agree on, the latest reset breaking ties, so a
    /// one-off reset (a plan change) does not throw it off.
    /// </summary>
    private static (DateTimeOffset Latest, TimeSpan Width)? WeeklyAnchor(List<(DateTimeOffset Before, DateTimeOffset After)> drops)
    {
        if (drops.Count == 0)
        {
            return null;
        }

        var week = WeeklyWindow.Ticks;
        long Phase(DateTimeOffset t) => ((t.UtcTicks % week) + week) % week;
        bool Covers((DateTimeOffset Before, DateTimeOffset After) d, long phase)
        {
            var back = ((Phase(d.After) - phase) % week + week) % week;
            return back < (d.After - d.Before).Ticks;
        }

        // The best point is always the earliest end among the resets that agree on it, so it is
        // the upper bound of their overlap; the overlap starts at the latest of their starts.
        var best = drops
            .Select((d, index) => (Drop: d, Index: index, Votes: drops.Count(other => Covers(other, Phase(d.After)))))
            .OrderByDescending(c => c.Votes)
            .ThenByDescending(c => c.Index)
            .First();
        var x = Phase(best.Drop.After);
        var width = drops
            .Where(d => Covers(d, x))
            .Min(d => (d.After - d.Before).Ticks - (((Phase(d.After) - x) % week) + week) % week);
        return (best.Drop.After, TimeSpan.FromTicks(width));
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static int? Percent(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var number)
            || double.IsNaN(number) || double.IsInfinity(number))
        {
            return null;
        }

        return (int)Math.Clamp(Math.Round(number), 0, 100);
    }
}
