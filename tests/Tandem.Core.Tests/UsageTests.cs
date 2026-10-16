using System.Text.Json;
using Tandem.Core.Profiles;
using Tandem.Core.Usage;

namespace Tandem.Core.Tests;

public sealed class UsageTests : IDisposable
{
    // A Thursday, in UTC.
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static UsageSample S(DateTimeOffset at, int? fiveHour, int? weekly, string? org = "org-a") => new(at, fiveHour, weekly, org);

    private static List<(DateTimeOffset At, int Value)> Series(params (double Hours, int Value)[] points) =>
        points.Select(p => (T0.AddHours(p.Hours), p.Value)).ToList();

    [Fact]
    public void Samples_are_parsed_leniently()
    {
        using var document = JsonDocument.Parse("""
            { "version": 2, "samples": [
              { "org": "a", "t": 1792108800000, "u": { "fh": 45, "sd": 51 } },
              { "org": "a", "t": 1792108800, "u": { "fh": 12.6 } },
              { "org": "a", "t": 1792108900000, "u": { "fh": 140, "sd": -3 } },
              { "org": "a", "t": 1792109000000, "u": { "sd": "high" } },
              { "t": 1792109100000, "u": { "fh": 3, "sd": 4 } },
              { "org": "a", "u": { "fh": 1, "sd": 1 } },
              { "org": "a", "t": 99999999999999999, "u": { "fh": 1, "sd": 1 } },
              "junk"
            ] }
            """);

        var samples = UsageReader.ParseSamples(document.RootElement);

        Assert.Equal(4, samples.Count);
        Assert.Equal(new UsageSample(DateTimeOffset.FromUnixTimeMilliseconds(1792108800000), 45, 51, "a"), samples[0]);
        Assert.Equal(new UsageSample(DateTimeOffset.FromUnixTimeSeconds(1792108800), 13, null, "a"), samples[1]);
        Assert.Equal((100, 0), (samples[2].FiveHour, samples[2].Weekly));
        Assert.Null(samples[3].Org);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "samples": {} }""")]
    [InlineData("""{ "samples": [] }""")]
    public void Unexpected_shapes_give_no_samples(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Empty(UsageReader.ParseSamples(document.RootElement));
    }

    [Fact]
    public void Five_hour_window_ends_five_hours_after_first_use()
    {
        // No use at 10:00, first use seen at 10:15, still growing at 11:00.
        var series = Series((10, 0), (10.25, 10), (11, 30));

        var status = UsageReader.FiveHourStatus(series, T0.AddHours(12))!;

        Assert.Equal(30, status.Percent);
        Assert.False(status.Reset);
        Assert.Equal(T0.AddHours(15.25), status.ResetsAt);
        Assert.Equal(TimeSpan.FromMinutes(15), status.Uncertainty);
    }

    [Fact]
    public void Five_hour_window_counts_as_reset_once_it_ended()
    {
        var series = Series((10, 0), (10.25, 10), (11, 100));

        var status = UsageReader.FiveHourStatus(series, T0.AddHours(15.5))!;

        Assert.Equal(0, status.Percent);
        Assert.Equal(100, status.LastSeen);
        Assert.True(status.Reset);
        Assert.Null(status.ResetsAt);
    }

    [Fact]
    public void Five_hour_window_after_a_drop_starts_at_the_drop()
    {
        var series = Series((8, 60), (9, 100), (9.5, 20), (10, 25));

        var status = UsageReader.FiveHourStatus(series, T0.AddHours(10.5))!;

        Assert.Equal(25, status.Percent);
        Assert.Equal(T0.AddHours(14.5), status.ResetsAt);
        Assert.Equal(TimeSpan.FromMinutes(30), status.Uncertainty);
    }

    [Fact]
    public void Five_hour_run_longer_than_a_window_falls_back_to_the_latest_sample()
    {
        // Use never fell, yet more than five hours passed: a reset went unseen.
        var series = Series((1, 10), (3, 20), (5, 30), (7, 40));

        var status = UsageReader.FiveHourStatus(series, T0.AddHours(8))!;

        Assert.Equal(40, status.Percent);
        Assert.Equal(T0.AddHours(12), status.ResetsAt);
        Assert.Equal(UsageReader.FiveHourWindow, status.Uncertainty);
    }

    [Fact]
    public void Old_five_hour_value_is_reset_even_without_a_known_start()
    {
        var status = UsageReader.FiveHourStatus(Series((0, 70)), T0.AddHours(5))!;

        Assert.True(status.Reset);
        Assert.Equal(0, status.Percent);
    }

    [Fact]
    public void Weekly_reset_time_is_narrowed_down_across_weeks()
    {
        // Thursday resets seen with wide gaps (the computer was off at night), plus a one-off
        // reset on a Wednesday that must not throw the estimate off.
        var series = new List<(DateTimeOffset At, int Value)>();
        void Drop(DateTimeOffset before, DateTimeOffset after)
        {
            series.Add((before, 95));
            series.Add((after, 2));
        }

        DateTimeOffset At(int day, int hour, int minute) => T0.AddDays(day - 28).AddMinutes((hour * 60) + minute);
        Drop(At(0, 0, 42), At(0, 20, 42));    // Thu 00:42 – 20:42
        Drop(At(6, 21, 12), At(6, 21, 30));   // one-off, Wed 21:12 – 21:30
        Drop(At(7, 0, 30), At(7, 19, 0));     // Thu 00:30 – 19:00
        Drop(At(13, 23, 54), At(14, 16, 36)); // Wed 23:54 – Thu 16:36
        Drop(At(20, 19, 0), At(21, 16, 15));  // Wed 19:00 – Thu 16:15
        series.Add((At(26, 0, 0), 40));

        var status = UsageReader.WeeklyStatus(series, At(27, 0, 0))!;

        Assert.Equal(40, status.Percent);
        Assert.Equal(At(28, 16, 15), status.ResetsAt);
        Assert.Equal(TimeSpan.FromMinutes((16 * 60) + 15 - 42), status.Uncertainty);
    }

    [Fact]
    public void Weekly_reset_already_seen_this_week_points_to_next_week()
    {
        // Reset seen between Thu 01:00 and 03:00, latest sample at 05:00 that same Thursday.
        var series = Series((-167, 80), (-166, 90), (1, 90), (3, 1), (5, 4));

        var status = UsageReader.WeeklyStatus(series, T0.AddHours(6))!;

        Assert.Equal(4, status.Percent);
        Assert.Equal(T0.AddHours(3 + 168), status.ResetsAt);
    }

    [Fact]
    public void Weekly_without_a_seen_reset_has_no_reset_time()
    {
        var series = Series((0, 10), (24, 30), (48, 45));

        var status = UsageReader.WeeklyStatus(series, T0.AddHours(50))!;
        Assert.Equal(45, status.Percent);
        Assert.Null(status.ResetsAt);

        var later = UsageReader.WeeklyStatus(series, T0.AddHours(48 + 168))!;
        Assert.True(later.Reset);
        Assert.Equal(0, later.Percent);
    }

    [Fact]
    public void Weekly_reset_passes_at_the_estimated_time()
    {
        var series = Series((-168, 90), (-167.5, 3), (-1, 60));

        Assert.Equal(60, UsageReader.WeeklyStatus(series, T0.AddMinutes(29))!.Percent);
        var after = UsageReader.WeeklyStatus(series, T0.AddMinutes(30))!;
        Assert.True(after.Reset);
        Assert.Equal(0, after.Percent);
    }

    [Fact]
    public void Usage_is_merged_per_account_across_folders()
    {
        var now = T0.AddHours(12);
        var overview = UsageReader.Combine(
        [
            // The original folder holds an older account (org-a) and the one it is signed in to now (org-b).
            ("default", [S(T0.AddHours(9), 0, 40), S(T0.AddHours(9.25), 20, 41), S(T0.AddHours(11), 5, 10, "org-b")]),
            // The added account is signed in to org-a and saw newer numbers.
            ("work1234", [S(T0.AddHours(11.5), 60, 47)]),
            ("empty123", []),
        ], now);

        Assert.Equal(5, overview.For("default")!.FiveHour!.Percent);
        var work = overview.For("work1234")!;
        Assert.Equal(60, work.FiveHour!.Percent);
        Assert.Equal(47, work.Weekly!.Percent);
        Assert.Equal(T0.AddHours(14.25), work.FiveHour.ResetsAt);
        Assert.Equal(3, work.History.Count);
        Assert.Null(overview.For("empty123"));
        Assert.Empty(overview.SameAccount);
    }

    [Fact]
    public void Folders_signed_in_to_the_same_account_are_reported()
    {
        var overview = UsageReader.Combine(
        [
            ("default", [S(T0, 10, 10, "org-a")]),
            ("work1234", [S(T0.AddMinutes(1), 12, 10, "org-a")]),
            ("home1234", [S(T0, 10, 10, "org-c")]),
            ("noorg123", [S(T0, 10, 10, null)]),
            ("noorg456", [S(T0, 10, 10, null)]),
        ], T0.AddHours(1));

        var group = Assert.Single(overview.SameAccount);
        Assert.Equal(new[] { "default", "work1234" }, group.Order());
        Assert.Equal(12, overview.For("default")!.FiveHour!.Percent);
    }

    [Fact]
    public void Recommendation_prefers_the_most_room_left()
    {
        var now = T0.AddHours(1);
        var overview = UsageReader.Combine(
        [
            ("aaaaaaaa", [S(T0, 20, 85, "a")]),
            ("bbbbbbbb", [S(T0, 40, 30, "b")]),
            ("cccccccc", [S(T0, 100, 10, "c")]),
        ], now);

        var pick = UsageReader.Recommend(overview, ["aaaaaaaa", "bbbbbbbb", "cccccccc", "dddddddd"])!;

        Assert.Equal("bbbbbbbb", pick.ProfileId);
        Assert.False(pick.AllLimited);
        Assert.Null(UsageReader.Recommend(overview, ["dddddddd"]));
    }

    [Fact]
    public void When_all_are_limited_the_one_back_first_is_recommended()
    {
        var now = T0.AddHours(2);
        var overview = UsageReader.Combine(
        [
            ("aaaaaaaa", [S(T0, 0, 50, "a"), S(T0.AddMinutes(15), 100, 50, "a")]),
            ("bbbbbbbb", [S(T0.AddHours(1), 0, 50, "b"), S(T0.AddHours(1.25), 100, 50, "b")]),
        ], now);

        var pick = UsageReader.Recommend(overview, ["bbbbbbbb", "aaaaaaaa"])!;

        Assert.True(pick.AllLimited);
        Assert.Equal("aaaaaaaa", pick.ProfileId);
        Assert.Equal(T0.AddHours(5.25), pick.AvailableAt);
    }

    [Fact]
    public void File_is_read_while_Claude_has_it_open()
    {
        var folder = Path.Combine(_temp.Path, "profile");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, UsageReader.FileName);
        File.WriteAllText(file, """{ "version": 2, "samples": [ { "org": "a", "t": 1792108800000, "u": { "fh": 45, "sd": 51 } } ] }""");
        var profile = new Profile { Id = "abcdefgh", DisplayName = "Work", Color = "#3B82C4", DataFolder = folder };

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var sample = Assert.Single(UsageReader.ReadSamples(profile));
            Assert.Equal(45, sample.FiveHour);
        }

        File.WriteAllText(file, "{ not json");
        Assert.Empty(UsageReader.ReadSamples(profile));
        Assert.Empty(UsageReader.ReadSamples(profile with { DataFolder = Path.Combine(_temp.Path, "missing") }));
    }
}
