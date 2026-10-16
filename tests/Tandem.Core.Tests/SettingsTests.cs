using Tandem.Core.Settings;

namespace Tandem.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private static readonly PixelRect Frame = new(100, 50, 1100, 850); // 1000 x 800
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Defaults_keep_the_button_off_and_at_the_top_right()
    {
        var settings = new AppSettings();

        Assert.False(settings.OverlayEnabled);
        Assert.Equal(OverlayCorner.TopRight, settings.OverlayCorner);
        Assert.True(settings.OpenInSamePlace);
        Assert.Equal(SwitchBehavior.LeaveOpen, settings.SwitchBehavior);
        Assert.Equal(ThemeMode.System, settings.Theme);
    }

    [Fact]
    public void Version_1_settings_are_migrated()
    {
        var paths = _temp.CreatePaths();
        Directory.CreateDirectory(paths.RoamingDir);
        File.WriteAllText(paths.SettingsFile, """
            { "version": 1, "overlayEnabled": true, "overlayOffsetX": 12, "overlayOffsetY": 12, "closeOtherOnSwitch": true, "hotkey": "Ctrl+Alt+K" }
            """);

        var settings = SettingsStore.Load(paths).Current;

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.False(settings.OverlayEnabled);
        Assert.Equal(OverlayCorner.TopRight, settings.OverlayCorner);
        Assert.Equal(SwitchBehavior.Close, settings.SwitchBehavior);
        Assert.Null(settings.CloseOtherOnSwitch);
        Assert.Equal("Ctrl+Alt+K", settings.Hotkey);
    }

    [Fact]
    public void Settings_round_trip_through_the_file()
    {
        var paths = _temp.CreatePaths();
        var store = SettingsStore.Load(paths);

        store.Update(s =>
        {
            s.Theme = ThemeMode.Dark;
            s.OverlayEnabled = true;
            s.ResetOverlay(OverlayCorner.BottomLeft);
            s.SwitchBehavior = SwitchBehavior.Minimize;
        });
        var reloaded = SettingsStore.Load(paths).Current;

        Assert.Equal(ThemeMode.Dark, reloaded.Theme);
        Assert.True(reloaded.OverlayEnabled);
        Assert.Equal(OverlayCorner.BottomLeft, reloaded.OverlayCorner);
        Assert.Equal(SwitchBehavior.Minimize, reloaded.SwitchBehavior);
    }

    [Theory]
    [InlineData(OverlayCorner.TopLeft, 1.0, 120, 56)]
    [InlineData(OverlayCorner.TopRight, 1.0, 980, 56)]
    [InlineData(OverlayCorner.BottomLeft, 1.0, 120, 814)]
    [InlineData(OverlayCorner.BottomRight, 1.0, 980, 814)]
    [InlineData(OverlayCorner.TopRight, 1.5, 970, 59)]
    public void Button_is_placed_from_its_corner(OverlayCorner corner, double scale, int x, int y)
    {
        // 100 x 30 px button, offsets 20 / 6 DIPs from the corner's edges.
        Assert.Equal((x, y), OverlayGeometry.Place(Frame, 100, 30, corner, 20, 6, scale));
    }

    [Fact]
    public void Button_never_leaves_the_window()
    {
        Assert.Equal((100, 50), OverlayGeometry.Place(Frame, 100, 30, OverlayCorner.TopRight, 5000, 0, 1));
        Assert.Equal((1000, 820), OverlayGeometry.Place(Frame, 100, 30, OverlayCorner.TopLeft, 5000, 5000, 1));
        var tiny = new PixelRect(0, 0, 50, 20);
        Assert.Equal((0, 0), OverlayGeometry.Place(tiny, 100, 30, OverlayCorner.BottomRight, 10, 10, 1));
    }

    [Theory]
    [InlineData(130, 60, OverlayCorner.TopLeft, 30, 10)]
    [InlineData(950, 60, OverlayCorner.TopRight, 50, 10)]
    [InlineData(130, 800, OverlayCorner.BottomLeft, 30, 20)]
    [InlineData(950, 800, OverlayCorner.BottomRight, 50, 20)]
    public void Dragging_attaches_the_button_to_the_nearest_corner(int left, int top, OverlayCorner corner, double x, double y)
    {
        var button = new PixelRect(left, top, left + 100, top + 30);

        Assert.Equal((corner, x, y), OverlayGeometry.Anchor(Frame, button, 1));
    }

    [Fact]
    public void Anchor_then_place_returns_the_same_spot_at_any_scale()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var button = new PixelRect(900, 70, 1000, 100);
            var (corner, x, y) = OverlayGeometry.Anchor(Frame, button, scale);

            Assert.Equal((900, 70), OverlayGeometry.Place(Frame, 100, 30, corner, x, y, scale));
        }
    }
}
