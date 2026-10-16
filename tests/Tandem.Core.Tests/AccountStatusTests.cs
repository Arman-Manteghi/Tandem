using Tandem.Core.Profiles;
using Tandem.Core.Routing;
using Tandem.Core.Settings;

namespace Tandem.Core.Tests;

public sealed class AccountStatusTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Plan_is_stored_and_updated()
    {
        var paths = _temp.CreatePaths();
        var store = ProfileStore.Load(paths, new FixedClock(T0));

        var work = store.Add("Work", "#3B82C4", AccountPlan.Max);
        store.Update(Profile.DefaultId, plan: AccountPlan.Pro);

        var reloaded = ProfileStore.Load(paths);
        Assert.Equal(AccountPlan.Max, reloaded.Find(work.Id)!.Plan);
        Assert.Equal(AccountPlan.Pro, reloaded.Default.Plan);
        Assert.Contains("\"plan\": \"max\"", File.ReadAllText(paths.ProfilesFile));
    }

    [Fact]
    public void Unknown_enum_values_do_not_make_files_unreadable()
    {
        var paths = _temp.CreatePaths();
        Directory.CreateDirectory(paths.RoamingDir);
        File.WriteAllText(paths.ProfilesFile, """
            { "version": 1, "profiles": [ { "id": "abcdefgh", "displayName": "Work", "color": "#3B82C4", "plan": "ultra-plus" } ] }
            """);
        File.WriteAllText(paths.SettingsFile, """
            { "version": 2, "theme": "neon", "overlayCorner": "middle", "hotkey": "Ctrl+Alt+K" }
            """);

        var store = ProfileStore.Load(paths);
        var settings = SettingsStore.Load(paths).Current;

        Assert.Null(store.LoadWarning);
        Assert.Equal("Work", store.Find("abcdefgh")!.DisplayName);
        Assert.Equal(AccountPlan.Unknown, store.Find("abcdefgh")!.Plan);
        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.Equal("Ctrl+Alt+K", settings.Hotkey);
    }

    [Fact]
    public void Last_used_is_tracked_per_account_and_forgotten_on_delete()
    {
        var state = new RuntimeState()
            .WithUse("workwork", T0)
            .WithUse("homehome", T0.AddHours(1))
            with { PendingSignIn = new PendingSignIn("workwork", T0.AddMinutes(5)) };

        Assert.Equal(T0, state.LastUsedOf("workwork"));
        Assert.Equal("homehome", state.LastActiveProfileId);

        var after = state.Without("workwork");

        Assert.Null(after.LastUsedOf("workwork"));
        Assert.Null(after.PendingSignIn);
        Assert.Equal(T0.AddHours(1), after.LastUsedOf("homehome"));
        Assert.Equal("homehome", after.LastActiveProfileId);
    }

    [Fact]
    public void Last_used_survives_a_round_trip_through_the_state_file()
    {
        var paths = _temp.CreatePaths();
        var store = new StateStore(paths);

        store.Update(s => s.WithUse("workwork", T0));

        Assert.Equal(T0, store.Read().LastUsedOf("workwork"));
    }

    [Theory]
    [InlineData(1, new[] { 0, 0, 1200, 900 })]
    [InlineData(2, new[] { 0, 0, 600, 900, 600, 0, 1200, 900 })]
    [InlineData(3, new[] { 0, 0, 400, 900, 400, 0, 800, 900, 800, 0, 1200, 900 })]
    [InlineData(4, new[] { 0, 0, 600, 450, 600, 0, 1200, 450, 0, 450, 600, 900, 600, 450, 1200, 900 })]
    public void Windows_are_tiled_to_fill_the_work_area(int count, int[] expected)
    {
        var cells = WindowLayout.Grid(new PixelRect(0, 0, 1200, 900), count);

        Assert.Equal(expected, cells.SelectMany(c => new[] { c.Left, c.Top, c.Right, c.Bottom }));
    }

    [Fact]
    public void A_short_last_row_is_stretched()
    {
        var cells = WindowLayout.Grid(new PixelRect(0, 0, 1200, 900), 5);

        Assert.Equal(5, cells.Count);
        Assert.All(cells.Take(3), c => Assert.Equal(400, c.Width));
        Assert.All(cells.Skip(3), c => Assert.Equal(600, c.Width));
        Assert.Equal(900, cells[^1].Bottom);
    }

    [Fact]
    public void Gaps_separate_neighbours_but_not_the_outer_edges()
    {
        var cells = WindowLayout.Grid(new PixelRect(100, 50, 1300, 950), 2, gap: 8);

        Assert.Equal(new PixelRect(100, 50, 696, 950), cells[0]);
        Assert.Equal(new PixelRect(704, 50, 1300, 950), cells[1]);
    }

    [Fact]
    public void Nothing_to_tile_returns_no_cells()
    {
        Assert.Empty(WindowLayout.Grid(new PixelRect(0, 0, 1200, 900), 0));
        Assert.Empty(WindowLayout.Grid(new PixelRect(0, 0, 0, 0), 3));
    }
}
