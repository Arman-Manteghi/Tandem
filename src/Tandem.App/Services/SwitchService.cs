using System.IO;
using System.Windows.Threading;
using Tandem.App.Native;
using Tandem.Core;
using Tandem.Core.Claude;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;
using Tandem.Core.Routing;
using Tandem.Core.Safety;
using Tandem.Core.Settings;

namespace Tandem.App.Services;

/// <summary>
/// Switching = bring the account's window to the front, or start it if it is not running.
/// Optionally the account opens exactly where the current window is, and the window you
/// leave is minimized or closed, so it feels like one window changing accounts.
/// </summary>
internal sealed class SwitchService(
    InstanceTracker instances,
    ProfileStore profiles,
    AppPaths paths,
    StateStore state,
    SettingsStore settings,
    Notifier notifier,
    Dispatcher dispatcher)
{
    private static readonly TimeSpan LaunchDebounce = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, DateTime> _recentLaunches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fire and forget: errors are reported to the user, never thrown.</summary>
    /// <param name="fromWindow">The Claude window the user is switching away from, if any.</param>
    public async void SwitchTo(Profile target, Profile? from, IntPtr fromWindow)
    {
        try
        {
            var options = settings.Current;
            var leaving = from is not null && from.Id != target.Id && fromWindow != IntPtr.Zero ? fromWindow : IntPtr.Zero;
            var spot = options.OpenInSamePlace ? WindowArrange.Capture(leaving) : null;

            await ShowTargetAsync(target, (hwnd, justOpened) =>
            {
                // A window that just opened takes the current window's place. An account that was
                // already open keeps the spot you gave it, unless you work in one window at a time
                // (the window you leave is minimized or closed).
                if (spot is not null && (justOpened || options.SwitchBehavior != SwitchBehavior.LeaveOpen))
                {
                    WindowArrange.Apply(hwnd, spot);
                }

                WindowTools.Activate(hwnd);
                if (leaving != IntPtr.Zero && leaving != hwnd)
                {
                    LeaveWindow(from!, leaving, options.SwitchBehavior);
                }
            });
            RememberActive(target);
        }
        catch (Exception ex)
        {
            ReportLaunchError(target, ex);
        }
    }

    /// <summary>
    /// Starts (or focuses) a profile through the official app. An added account first gets a
    /// working Claude Code program if it lacks one and, when closed, the original account's
    /// current app setup (off the UI thread).
    /// </summary>
    public async Task<LaunchResult> LaunchAsync(Profile profile)
    {
        _recentLaunches[profile.Id] = DateTime.UtcNow;
        try
        {
            var options = settings.Current;
            var all = profiles.Profiles;
            await Task.Run(() => AccountLauncher.Prepare(profile, all, paths, options));

            // Allow the app (or its single-instance hand-off) to take the foreground.
            User32.AllowSetForegroundWindow(User32.ASFW_ANY);
            var result = ClaudeLauncher.Launch(profile);
            if (!profile.IsDefault && profile.DataFolder is not null)
            {
                VerifyFolderInUse(profile);
            }

            return result;
        }
        catch
        {
            // A failed launch must not block the next attempt.
            _recentLaunches.Remove(profile.Id);
            throw;
        }
    }

    public bool IsLaunching(Profile profile) =>
        _recentLaunches.TryGetValue(profile.Id, out var at) && DateTime.UtcNow - at < LaunchDebounce;

    /// <summary>Records that the user is in this account now (for "last used" and link routing).</summary>
    public void RememberActive(Profile profile)
    {
        try
        {
            state.Update(s => s.WithUse(profile.Id, DateTimeOffset.UtcNow));
        }
        catch (IOException ex)
        {
            Log.Warn("Could not save the last active account.", ex);
        }
    }

    public void ExpectSignIn(Profile profile) =>
        state.Update(s => s with { PendingSignIn = new PendingSignIn(profile.Id, DateTimeOffset.UtcNow + StateStore.SignInWindow) });

    /// <summary>Asks every window of the profile to close (like clicking X). Never kills the process.</summary>
    public bool RequestClose(Profile profile)
    {
        var running = instances.FindRunning(profile);
        if (running is null)
        {
            return false;
        }

        foreach (var hwnd in WindowTools.FindAppWindows(running.ProcessId))
        {
            User32.PostMessage(hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        Log.Info($"Asked profile {profile.Id} to close its windows.");
        return true;
    }

    /// <summary>
    /// Closes an added account's Claude completely: first like clicking X on its windows; if it
    /// keeps running in the background (tray), its process tree is ended, like Quit in Claude's
    /// tray menu. Only the instance started with this account's data folder is affected, and
    /// never the original account. Callers ask the user first.
    /// </summary>
    public async Task<bool> QuitCompletelyAsync(Profile profile)
    {
        if (profile.IsDefault)
        {
            return false;
        }

        RequestClose(profile);
        if (await WaitUntilClosedAsync(profile, TimeSpan.FromSeconds(6)))
        {
            return true;
        }

        if (instances.FindRunning(profile) is { } running)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(running.ProcessId);

                // Guard against a reused process id: it must be the very process we inspected.
                if (Math.Abs((process.StartTime.ToUniversalTime() - running.Process.StartTimeUtc).TotalSeconds) < 2)
                {
                    process.Kill(entireProcessTree: true);
                    Log.Info($"Ended the background Claude of profile {profile.Id}.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Warn($"Could not end the background Claude of profile {profile.Id}.", ex);
            }
        }

        return await WaitUntilClosedAsync(profile, TimeSpan.FromSeconds(8));
    }

    /// <summary>Waits (without blocking the UI) until the profile has no running instance.</summary>
    public async Task<bool> WaitUntilClosedAsync(Profile profile, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (instances.FindRunning(profile) is null)
            {
                return true;
            }

            await Task.Delay(400);
        }

        return instances.FindRunning(profile) is null;
    }

    public void ReportLaunchError(Profile profile, Exception ex)
    {
        switch (ex)
        {
            case ClaudeNotInstalledException:
                notifier.Error("Claude is not installed", "Install the Claude desktop app for this Windows user, then try again.");
                break;
            case VirtualizedFolderException:
                notifier.Error("Start Tandem from the Start menu", ex.Message);
                break;
            default:
                Log.Error($"Could not open profile {profile.Id}.", ex);
                notifier.Error("Could not open the account", ex.Message);
                break;
        }
    }

    /// <summary>Focuses the profile's window, starting the profile first if needed.</summary>
    private async Task ShowTargetAsync(Profile target, Action<IntPtr, bool> onWindow)
    {
        var running = instances.FindRunning(target);
        if (running is not null)
        {
            var hwnd = WindowTools.FindMainWindow(running.ProcessId);
            if (hwnd != IntPtr.Zero)
            {
                onWindow(hwnd, false);
                Log.Info($"Switched to running profile {target.Id}.");
                return;
            }

            // Running without a visible window (for example closed to the tray): the app's own
            // single-instance hand-off shows it again.
            await LaunchAsync(target);
            WhenWindowAppears(target, hwnd => onWindow(hwnd, true));
            return;
        }

        if (_recentLaunches.TryGetValue(target.Id, out var at) && DateTime.UtcNow - at < LaunchDebounce)
        {
            Log.Info($"Ignored a repeated launch of profile {target.Id}.");
            return;
        }

        await LaunchAsync(target);
        WhenWindowAppears(target, hwnd => onWindow(hwnd, true));
    }

    private void LeaveWindow(Profile from, IntPtr hwnd, SwitchBehavior behavior)
    {
        switch (behavior)
        {
            case SwitchBehavior.Minimize:
                WindowArrange.MinimizeWithoutActivating(hwnd);
                break;
            case SwitchBehavior.Close:
                RequestClose(from);
                break;
        }
    }

    private void WhenWindowAppears(Profile profile, Action<IntPtr> action)
    {
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - started > WindowWait)
            {
                timer.Stop();
                return;
            }

            if (instances.FindRunning(profile) is { } running && WindowTools.FindMainWindow(running.ProcessId) is var hwnd && hwnd != IntPtr.Zero)
            {
                timer.Stop();
                action(hwnd);
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Chromium creates a "lockfile" in the data folder it uses. If it does not appear, the app may
    /// have ignored --user-data-dir (a future change) and opened the Default account instead.
    /// Only the file's existence is checked.
    /// </summary>
    private void VerifyFolderInUse(Profile profile)
    {
        var lockfile = Path.Combine(profile.DataFolder!, "lockfile");
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (File.Exists(lockfile))
            {
                timer.Stop();
                return;
            }

            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(25))
            {
                timer.Stop();
                Log.Warn($"Profile {profile.Id} did not start using its data folder.");
                notifier.Warning(
                    "The account may not have opened",
                    $"Claude did not start using the folder of \"{profile.DisplayName}\". If a window opened, check that it shows the right account.");
            }
        };
        timer.Start();
    }
}
