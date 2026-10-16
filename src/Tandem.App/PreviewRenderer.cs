using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tandem.App.Views;
using Tandem.Core;
using Tandem.Core.Profiles;
using Tandem.Core.Usage;

namespace Tandem.App;

/// <summary>
/// Developer tool: <c>Tandem.exe --render-previews &lt;folder&gt; [light|dark]</c> renders Tandem's
/// windows off-screen to PNG files (used for the README screenshots and for UI review).
/// It uses sample accounts in a temporary folder and never touches real data or the Claude app.
/// </summary>
internal static class PreviewRenderer
{
    public static void Run(App app, string outputDir, bool dark)
    {
        Directory.CreateDirectory(outputDir);
        var root = Path.Combine(Path.GetTempPath(), "tandem-preview", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(Path.Combine(root, "Roaming"), Path.Combine(root, "Local"));
        ProfileRow.PathAliases.Insert(0, (Path.Combine(root, "Local"), "%LOCALAPPDATA%"));
        var store = ProfileStore.Load(paths);
        store.Update(Profile.DefaultId, "Personal", "#E07A5F", AccountPlan.Pro);
        var work = store.Add("Work", "#3B82C4", AccountPlan.Max);
        var side = store.Add("Side project", "#2A9D8F", AccountPlan.Free);
        new Core.Routing.StateStore(paths).Update(s => s
            .WithUse(side.Id, DateTimeOffset.UtcNow.AddDays(-3))
            .WithUse(work.Id, DateTimeOffset.UtcNow.AddHours(-2))
            .WithUse(Profile.DefaultId, DateTimeOffset.UtcNow));

        var host = new AppHost(app, paths);
        app.Host = host;
        host.Theme.Force(dark);
        var suffix = dark ? "dark" : "light";

        // Made-up usage, so no real usage file is ever read for the screenshots.
        var now = DateTimeOffset.UtcNow;
        var usage = UsageReader.Combine(
        [
            (Profile.DefaultId, SampleUsage("a", 11, now, fiveHourNow: 72, windowAge: TimeSpan.FromHours(3.2), weeklyNow: 64, sinceWeeklyReset: TimeSpan.FromDays(4.6))),
            (work.Id, SampleUsage("b", 23, now, fiveHourNow: 18, windowAge: TimeSpan.FromHours(0.8), weeklyNow: 31, sinceWeeklyReset: TimeSpan.FromDays(2.3))),
            (side.Id, SampleUsage("c", 37, now, fiveHourNow: 100, windowAge: TimeSpan.FromHours(3.7), weeklyNow: 88, sinceWeeklyReset: TimeSpan.FromDays(5.4))),
        ], now);
        host.Usage.Freeze(usage);

        foreach (var (page, height) in new[] { ("accounts", 1040), ("usage", 1480), ("switching", 900), ("signin", 860), ("general", 640), ("about", 640) })
        {
            var manage = new ManageWindow(host) { Width = 1040, Height = height };
            manage.ShowPage(page);
            Save(manage, Path.Combine(outputDir, $"manage-{page}-{suffix}.png"));
        }

        var usageEnd = new ManageWindow(host) { Width = 1040, Height = 1000 };
        usageEnd.ShowPage("usage");
        Save(usageEnd, Path.Combine(outputDir, $"manage-usage-end-{suffix}.png"), w => ((ManageWindow)w).ScrollToEnd());

        var states = new Dictionary<string, Services.AccountRunState>
        {
            [Profile.DefaultId] = Services.AccountRunState.Open,
            [work.Id] = Services.AccountRunState.Open,
            [side.Id] = Services.AccountRunState.Background,
        };
        var menu = new SwitcherMenu(store.Profiles, Profile.DefaultId, states, usage, includeQuit: false, "Ctrl+Alt+A");
        Save(menu, Path.Combine(outputDir, $"menu-{suffix}.png"));

        var overlay = new OverlayWindow();
        overlay.SetProfile(work.DisplayName, work.Color);
        Save(overlay, Path.Combine(outputDir, $"button-{suffix}.png"));

        var add = ProfileDialog.ForAdd(null, store.SuggestColor(), store.Profiles, syncSetup: true, originalHasHistory: true, (_, _) => null);
        add.NameBox.Text = "Research";
        Save(add, Path.Combine(outputDir, $"add-{suffix}.png"));

        Save(SignInSetupDialog.ForPreview(host.ExePath), Path.Combine(outputDir, $"signin-setup-{suffix}.png"));

        host.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Temporary sample data; leaving it behind in %TEMP% is harmless.
        }
    }

    /// <summary>
    /// A week of plausible readings: the app open in the daytime, 5-hour windows that fill up and
    /// end, a weekly limit that grows and resets once, and a current window with the given numbers.
    /// </summary>
    private static List<UsageSample> SampleUsage(
        string org, int seed, DateTimeOffset now, int fiveHourNow, TimeSpan windowAge, int weeklyNow, TimeSpan sinceWeeklyReset)
    {
        var random = new Random(seed);
        var step = TimeSpan.FromMinutes(15);
        var start = now - TimeSpan.FromDays(7);
        var reset = now - sinceWeeklyReset;
        var windowStart = now - windowAge;
        var samples = new List<UsageSample>();
        DateTimeOffset? window = null;
        var fiveHour = 0;
        var weeklyBefore = 38 + random.Next(0, 10);
        for (var t = start; t <= now; t += step)
        {
            var inCurrent = t >= windowStart - step;
            var hour = t.ToLocalTime().Hour;
            if (!inCurrent && (hour < 9 || hour >= 23 || random.NextDouble() < 0.2))
            {
                continue;
            }

            if (inCurrent)
            {
                fiveHour = t < windowStart ? 0 : (int)Math.Round(fiveHourNow * Math.Min(1, (t - windowStart + step) / (now - windowStart + step)));
            }
            else
            {
                if (window is { } w && t >= w + UsageReader.FiveHourWindow)
                {
                    window = null;
                    fiveHour = 0;
                }

                if (random.NextDouble() < 0.5)
                {
                    window ??= t;
                    fiveHour = Math.Min(100, fiveHour + random.Next(2, 13));
                }
            }

            var weekly = t < reset
                ? (int)Math.Round(weeklyBefore + ((96 - weeklyBefore) * ((t - start) / (reset - start))))
                : (int)Math.Round(weeklyNow * Math.Min(1, (t - reset) / (now - reset)));
            samples.Add(new UsageSample(t, fiveHour, weekly, org));
        }

        return samples;
    }

    private static void Save(Window window, string file, Action<Window>? afterLayout = null)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 1;
        window.Show();
        window.UpdateLayout();
        afterLayout?.Invoke(window);

        // Let entrance animations (page fade, toggle thumbs) finish before capturing.
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        window.UpdateLayout();

        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        if (!window.AllowsTransparency)
        {
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            }

            bitmap.Render(background);
        }

        // Rendering the content itself keeps its layout offset (margins included).
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file))
        {
            encoder.Save(stream);
        }

        window.Close();
    }
}
