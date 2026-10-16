using System.Diagnostics;
using System.IO;
using System.Windows;
using Tandem.App.Native;
using Tandem.App.Services;
using Tandem.Core;
using Tandem.Core.Claude;
using Tandem.Core.Integration;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;
using Tandem.Core.Routing;
using Tandem.Core.Safety;
using Tandem.Core.Settings;
using Tandem.Core.Shell;

namespace Tandem.App;

/// <summary>
/// Entry point. Besides the tray app, Tandem.exe has two short-lived modes used by Windows:
/// <c>--open-url &lt;claude://...&gt;</c> (sign-in helper) and <c>--launch &lt;profile id&gt;</c> (shortcuts).
/// </summary>
public static class Program
{
    private static readonly TimeSpan RelaunchGuard = TimeSpan.FromSeconds(20);

    [STAThread]
    public static int Main(string[] args)
    {
        var paths = AppPaths.ForCurrentUser();

        // Inside Claude's process tree, files created under AppData would land in Claude's private
        // package storage. Decide that before Tandem writes anything, even a log file.
        var startedFromClaude = ProcessAncestry.IsStartedFromClaude();
        if (!startedFromClaude)
        {
            Log.Initialize(paths.LogsDir);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception.", e.ExceptionObject as Exception);

        try
        {
            if (args.Length >= 2 && args[0] == LinkHandlerRegistration.OpenUrlSwitch)
            {
                return OpenUrl(paths, args[1], canWrite: !startedFromClaude);
            }

            if (args.Length >= 2 && args[0] == Shortcuts.LaunchSwitch)
            {
                return LaunchProfile(paths, args[1], canWrite: !startedFromClaude);
            }

            if (args.Length >= 2 && args[0] == "--render-previews")
            {
                var app = new App(paths, null, false, (args[1], args.Length > 2 && args[2] == "dark"));
                app.InitializeComponent();
                return app.Run();
            }

            return RunTrayApp(paths, startedFromClaude, showManage: !args.Contains(Shortcuts.StartupSwitch));
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error.", ex);
            MessageBox.Show(ex.Message, "Tandem", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int RunTrayApp(AppPaths paths, bool startedFromClaude, bool showManage)
    {
        var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalExisting();
            return 0;
        }

        using (instance)
        {
            // Started from inside Claude (e.g. its terminal)? Restart through Explorer, outside
            // Claude's process tree, so the folders Tandem creates are real folders.
            if (startedFromClaude)
            {
                // Either relaunched, or (relaunch did not help) refused with an explanation.
                // Never run the tray app from in there.
                TryRelaunchOutsideClaude(instance);
                return 0;
            }

            var app = new App(paths, instance, showManage);
            app.InitializeComponent();
            return app.Run();
        }
    }

    /// <summary>Sign-in helper: forwards one claude:// link, unchanged, to the right account.</summary>
    /// <param name="canWrite">
    /// False inside Claude's package, where Windows would redirect Tandem's writes into the
    /// package's private storage: the link is still forwarded, nothing is saved.
    /// </param>
    private static int OpenUrl(AppPaths paths, string link, bool canWrite)
    {
        if (!ClaudeLink.IsValid(link))
        {
            Log.Warn("Ignored a malformed claude: link.");
            return 2;
        }

        var profiles = ProfileStore.Load(paths);
        var settings = SettingsStore.Load(paths).Current;
        var state = new StateStore(paths);
        LinkRoute route;
        if (canWrite)
        {
            LinkRoute? routed = null;
            state.Update(current =>
            {
                routed = LinkRouter.Route(profiles.Profiles, current, DateTimeOffset.UtcNow, settings.LinkFallback);
                return LinkRouter.AfterRouting(current, routed);
            });
            route = routed!;
        }
        else
        {
            route = LinkRouter.Route(profiles.Profiles, state.Read(), DateTimeOffset.UtcNow, settings.LinkFallback);
        }

        try
        {
            User32.AllowSetForegroundWindow(User32.ASFW_ANY);
            AccountLauncher.Launch(route.Profile, profiles.Profiles, paths, settings, link, prepare: canWrite);
            Log.Info($"Forwarded a link to profile {route.Profile.Id} ({route.Reason}).");
            return 0;
        }
        catch (Exception ex) when (ex is ClaudeNotInstalledException or VirtualizedFolderException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Forwarding a link failed.", ex);
            MessageBox.Show(ex.Message, "Tandem", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }

    /// <summary>Profile shortcut: open (or focus) one account, then make sure the tray app runs.</summary>
    private static int LaunchProfile(AppPaths paths, string profileId, bool canWrite)
    {
        var profiles = ProfileStore.Load(paths);
        if (profiles.Find(profileId) is not { } profile)
        {
            MessageBox.Show("This shortcut belongs to an account that was removed from Tandem.", "Tandem", MessageBoxButton.OK, MessageBoxImage.Information);
            return 3;
        }

        try
        {
            User32.AllowSetForegroundWindow(User32.ASFW_ANY);
            AccountLauncher.Launch(profile, profiles.Profiles, paths, SettingsStore.Load(paths).Current, prepare: canWrite);
            if (canWrite)
            {
                new StateStore(paths).Update(s => s with { LastActiveProfileId = profile.Id });
            }
        }
        catch (Exception ex) when (ex is ClaudeNotInstalledException or VirtualizedFolderException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Opening profile {profile.Id} from a shortcut failed.", ex);
            MessageBox.Show(ex.Message, "Tandem", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }

        if (!SingleInstance.IsRunning && Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe) { ArgumentList = { Shortcuts.StartupSwitch }, UseShellExecute = false });
        }

        return 0;
    }

    private static bool TryRelaunchOutsideClaude(SingleInstance instance)
    {
        // A marker in %TEMP% (not virtualized) prevents a relaunch loop.
        var marker = Path.Combine(Path.GetTempPath(), "tandem-relaunch.marker");
        if ((File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < RelaunchGuard)
            || Environment.ProcessPath is not { } exe)
        {
            MessageBox.Show(
                "Tandem was started from inside the Claude app (for example from its terminal). " +
                "Windows would then store Tandem's account folders inside Claude's private storage. " +
                "Please start Tandem from the Start menu or from Explorer.",
                "Tandem", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        Log.Info("Started from inside Claude; restarting through Explorer.");
        instance.Dispose();
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { exe }, UseShellExecute = false });
        return true;
    }
}
