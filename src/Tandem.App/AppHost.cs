using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Tandem.App.Native;
using Tandem.App.Services;
using Tandem.App.Views;
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

internal enum MenuOrigin
{
    Overlay,
    Tray,
    Hotkey,
}

/// <summary>Composition root of the tray app and the place where user actions are carried out.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly Application _app;
    private readonly Dispatcher _dispatcher;
    private ManageWindow? _manage;
    private SwitcherMenu? _menu;
    private readonly HashSet<string> _limitNotices = [];
    private string? _lastSavedActiveId;
    private DispatcherTimer? _helperWatch;

    public AppHost(Application app, AppPaths paths)
    {
        _app = app;
        _dispatcher = app.Dispatcher;
        Paths = paths;
        ExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Tandem.exe");

        Profiles = ProfileStore.Load(paths);
        Settings = SettingsStore.Load(paths);
        State = new StateStore(paths);
        Theme = new ThemeService(app, Settings.Current.Theme);
        Notifier = new Notifier();
        Tray = new TrayIconService();
        Notifier.Attach(Tray);
        Instances = new InstanceTracker(Profiles);
        Foreground = new ForegroundTracker(Instances, _dispatcher);
        Switcher = new SwitchService(Instances, Profiles, paths, State, Settings, Notifier, _dispatcher);
        Overlay = new OverlayController(Foreground, Instances, Settings, _dispatcher, pill => OpenSwitcher(MenuOrigin.Overlay, Overlay!.BoundsOf(pill)));
        Hotkey = new HotkeyService();
        Shortcuts = new Shortcuts(paths, ExePath);
        Usage = new UsageService(Profiles, _dispatcher);
    }

    public AppPaths Paths { get; }

    public string ExePath { get; }

    public ProfileStore Profiles { get; }

    public SettingsStore Settings { get; }

    public StateStore State { get; }

    public ThemeService Theme { get; }

    public Notifier Notifier { get; }

    public TrayIconService Tray { get; }

    public InstanceTracker Instances { get; }

    public ForegroundTracker Foreground { get; }

    public SwitchService Switcher { get; }

    public OverlayController Overlay { get; }

    public HotkeyService Hotkey { get; }

    public Shortcuts Shortcuts { get; }

    public UsageService Usage { get; }

    /// <summary>The account the user is in: the Claude window in front, else the one used last.</summary>
    public Profile CurrentProfile =>
        Foreground.Active?.Profile
        ?? Foreground.LastProfile
        ?? Profiles.Find(SafeReadState().LastActiveProfileId)
        ?? Profiles.Default;

    public LinkHandlerStatus HelperStatus => LinkHandlerRegistration.GetStatus(ExePath);

    public static bool IsClaudeInstalled => ClaudeLocator.Locate().IsInstalled;

    public void Start()
    {
        Tray.Clicked += (_, _) => OpenSwitcher(MenuOrigin.Tray);
        Tray.DoubleClicked += (_, _) => ShowManage();
        Hotkey.Pressed += (_, _) => OpenSwitcher(MenuOrigin.Hotkey);
        Foreground.ActiveChanged += (_, _) => OnActiveChanged();
        Settings.Changed += (_, _) => Theme.SetMode(Settings.Current.Theme);
        Profiles.Changed += (_, _) => _dispatcher.BeginInvoke(() =>
        {
            Foreground.RefreshProfiles();
            Tray.Update(CurrentProfile);
        });

        ApplyHotkey();
        Tray.Update(CurrentProfile);
        Tray.Show();
        Foreground.Refresh();
        Usage.Updated += OnUsageUpdated;
        Usage.Start();
        ProvideClaudeCode();

        var install = ClaudeLocator.Locate();
        var alias = install.AliasPath is null ? "off" : "on";
        Log.Info($"Tandem {AppVersion} started. Claude: {install.Kind} {install.Version?.ToString() ?? string.Empty}, alias {alias}.");
        if (!install.IsInstalled)
        {
            Notifier.Warning("Claude is not installed", "Install the Claude desktop app for this Windows user to use Tandem.");
        }

        // Keep the sign-in helper working: if Tandem.exe moved, point Tandem's own entry at the new
        // path (the user's choice in Windows Settings is not touched). Remind when it is not set up.
        var helper = HelperStatus;
        if (helper == LinkHandlerStatus.NeedsRepair)
        {
            try
            {
                LinkHandlerRegistration.Register(ExePath);
                Log.Info("Sign-in helper repaired after Tandem.exe moved.");
            }
            catch (Exception ex)
            {
                Log.Warn("Repairing the sign-in helper failed.", ex);
            }
        }
        else if (helper != LinkHandlerStatus.Active && Profiles.Profiles.Count > 1)
        {
            Notifier.Warning(
                "Finish the sign-in setup",
                "Without it, signing in to an added account ends up in your original Claude. Open Tandem → Sign-in to set it up once.");
        }

        if (Profiles.LoadWarning is { } warning)
        {
            _dispatcher.BeginInvoke(() => MessageDialog.Show(null, "Your account list was repaired", warning));
        }
    }

    public const string AuthorName = "Arman Manteghi";
    public const string AuthorUrl = "https://github.com/Arman-Manteghi";
    public const string ProjectUrl = AuthorUrl + "/Tandem";

    public static string AppVersion =>
        typeof(AppHost).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    // ------------------------------------------------------------------ windows

    public void ShowManage()
    {
        if (_manage is null)
        {
            _manage = new ManageWindow(this);
            _manage.Closed += (_, _) => _manage = null;
            _manage.Show();
        }

        if (_manage.WindowState == WindowState.Minimized)
        {
            _manage.WindowState = WindowState.Normal;
        }

        _manage.Activate();
    }

    public void OpenSwitcher(MenuOrigin origin, User32.RECT? buttonBounds = null)
    {
        _menu?.Close();
        User32.GetCursorPos(out var cursor);
        User32.RECT anchor;
        MenuPlacement placement;

        switch (origin)
        {
            case MenuOrigin.Overlay when buttonBounds is { } pill:
                anchor = pill;
                placement = MenuPlacement.NextToAnchor;
                break;
            case MenuOrigin.Hotkey when Foreground.Active is { } active:
                anchor = WindowTools.GetFrameBounds(active.Hwnd);
                placement = MenuPlacement.Centered;
                break;
            case MenuOrigin.Hotkey:
                anchor = WindowTools.GetWorkAreaAt(cursor.X, cursor.Y);
                placement = MenuPlacement.Centered;
                break;
            default:
                anchor = new User32.RECT { Left = cursor.X, Top = cursor.Y, Right = cursor.X, Bottom = cursor.Y };
                placement = MenuPlacement.AtPoint;
                break;
        }

        var current = CurrentProfile;
        var usage = Usage.ReadNow(TimeSpan.FromSeconds(30));
        var menu = new SwitcherMenu(Profiles.Profiles, current.Id, SafeStates(), usage, includeQuit: origin == MenuOrigin.Tray, Hotkey.Registered);
        Theme.Attach(menu);
        menu.ProfileChosen += profile => SwitchTo(profile);
        menu.AddRequested += () => AddAccount(null);
        menu.OpenAllRequested += () => _ = OpenAllAsync();
        menu.ArrangeRequested += () => ArrangeWindows(acrossScreens: false);
        menu.ManageRequested += ShowManage;
        menu.QuitRequested += Quit;
        menu.Closed += (_, _) =>
        {
            if (_menu == menu)
            {
                _menu = null;
            }
        };
        _menu = menu;
        menu.ShowAt(placement, anchor);
    }

    // ------------------------------------------------------------------ account actions

    public void SwitchTo(Profile profile) => Switcher.SwitchTo(profile, CurrentProfile, Foreground.CurrentWindow);

    /// <summary>
    /// Gives every account a window: closed ones start, ones running in the background show their
    /// window again. Each opens where it was last time.
    /// </summary>
    public async Task OpenAllAsync()
    {
        var states = SafeStates();
        var closed = Profiles.Profiles
            .Where(p => (!states.TryGetValue(p.Id, out var s) || s != AccountRunState.Open) && !Switcher.IsLaunching(p))
            .ToList();
        if (closed.Count == 0)
        {
            Notifier.Info("All accounts are open", "Every account already has its Claude window.");
            return;
        }

        foreach (var profile in closed)
        {
            try
            {
                await Switcher.LaunchAsync(profile);
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Switcher.ReportLaunchError(profile, ex);
            }
        }
    }

    /// <summary>Puts every open account window side by side, on this screen or across all screens.</summary>
    public void ArrangeWindows(bool acrossScreens)
    {
        var order = Profiles.Profiles.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var windows = Instances.Snapshot()
            .OrderBy(i => i.Profile is { } p && order.TryGetValue(p.Id, out var index) ? index : int.MaxValue)
            .Select(i => WindowTools.FindMainWindow(i.ProcessId))
            .Where(h => h != IntPtr.Zero)
            .ToList();
        if (windows.Count == 0)
        {
            Notifier.Info("No account windows are open", "Open some accounts first, for example with Open all.");
            return;
        }

        var arranged = WindowTiler.Arrange(windows, acrossScreens, Foreground.CurrentWindow);
        Log.Info($"Arranged {arranged} account window(s){(acrossScreens ? " across screens" : string.Empty)}.");
    }

    /// <summary>Fire and forget from UI handlers; every error is shown to the user.</summary>
    public async void AddAccount(Window? owner)
    {
        var syncSetup = Settings.Current.SyncSetupFromOriginal;
        var dialog = ProfileDialog.ForAdd(
            owner, Profiles.SuggestColor(), Profiles.Profiles, syncSetup, ProfileSetup.HasHistory(Profiles.Default),
            (name, color) => Profiles.Check(name, color));
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Profile profile;
        try
        {
            profile = Profiles.Add(dialog.ResultName, dialog.ResultColor, dialog.ResultPlan);
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException)
        {
            ShowError(owner, "Could not add the account", ex);
            return;
        }

        // Create the folder from Tandem's own process before Claude ever sees it, then bring the
        // chosen data over. Copies run off the UI thread; the new account is not running yet.
        var original = Profiles.Default;
        var importHistory = dialog.ImportHistory;
        var copyFrom = dialog.CopyFrom;
        var ownerHandle = OwnerHandle(owner);
        try
        {
            using (BusyCursor())
            {
                await Task.Run(() =>
                {
                    ProfileFolder.EnsureReady(profile.DataFolder!);
                    if (importHistory)
                    {
                        ProfileSetup.ImportHistory(original, profile);
                    }

                    if (copyFrom is not null)
                    {
                        ProfileSetup.Copy(copyFrom, profile, ownerHandle);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Preparing profile {profile.Id} failed.", ex);
            ShowError(owner, "The account was added, but its data could not be prepared", ex);
            if (ex is VirtualizedFolderException)
            {
                return;
            }
        }

        BeginSignIn(owner, profile, isNew: true);
    }

    /// <summary>Brings an account's Claude Code and Cowork history over from the original folder.</summary>
    public async Task ImportHistoryAsync(Window? owner, Profile target)
    {
        if (target.IsDefault)
        {
            return;
        }

        var choice = MessageDialog.Show(
            owner,
            "Bring Code history over?",
            $"Tandem copies Claude Code and Cowork history from your original Claude into “{target.DisplayName}”, so its sessions show up like they do there. " +
            "Files the account already has are kept; only newer copies replace older ones. Sign-in and device identity are never copied.",
            "Bring history",
            "Cancel",
            details: ProfileSetup.HistoryItems.Select(i => i.RelativePath));
        if (choice != DialogChoice.Primary || !await EnsureClosedAsync(owner, target, "Close and continue"))
        {
            return;
        }

        try
        {
            FileTransferReport report;
            using (BusyCursor())
            {
                var original = Profiles.Default;
                report = await Task.Run(() => ProfileSetup.ImportHistory(original, target));
            }

            MessageDialog.Show(
                owner,
                "History brought over",
                report.Updated == 0
                    ? $"“{target.DisplayName}” already had everything."
                    : $"{report.Updated} file(s) copied into “{target.DisplayName}”." +
                      (report.Skipped > 0 ? $" {report.Skipped} file(s) were in use and were skipped; try again later." : string.Empty));
        }
        catch (Exception ex)
        {
            Log.Error($"Importing history into profile {target.Id} failed.", ex);
            ShowError(owner, "Could not bring the history over", ex);
        }
    }

    public void SignInAgain(Window? owner, Profile profile) => BeginSignIn(owner, profile, isNew: false);

    public void EditAccount(Window? owner, Profile profile)
    {
        var dialog = ProfileDialog.ForEdit(owner, profile, (name, color) => Profiles.Check(name, color, profile.Id));
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var updated = Profiles.Update(profile.Id, dialog.ResultName, dialog.ResultColor, dialog.ResultPlan);
            if (File.Exists(Paths.ProfileIconFile(updated.Id)))
            {
                Shortcuts.EnsureProfileIcon(updated);
            }
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException)
        {
            ShowError(owner, "Could not save the account", ex);
        }
    }

    public async Task DeleteAccountAsync(Window? owner, Profile profile)
    {
        if (profile.IsDefault)
        {
            return;
        }

        var folder = profile.DataFolder;
        var managed = Paths.IsManagedProfileFolder(folder);
        var shortcuts = SafeFindShortcuts(profile);
        var items = new List<string>();
        if (managed && Directory.Exists(folder))
        {
            items.Add(folder!);
        }

        items.AddRange(shortcuts);
        var message = managed
            ? "Its data folder goes to the Recycle Bin, so you can still restore it. Your Claude account itself is not affected: you can add it again later and sign in."
            : "Its data folder is outside Tandem's folder, so Tandem leaves the folder where it is and only removes the entry.";
        if (MessageDialog.Show(owner, $"Delete “{profile.DisplayName}”?", message, "Delete", "Cancel", details: items, danger: true) != DialogChoice.Primary)
        {
            return;
        }

        if (!await EnsureClosedAsync(owner, profile, "Close and delete"))
        {
            return;
        }

        var ownerHandle = OwnerHandle(owner);
        try
        {
            if (managed && folder is not null && Directory.Exists(folder)
                && RecycleBin.Move(folder, ownerHandle) != RecycleResult.Moved)
            {
                MessageDialog.Show(owner, "Nothing was deleted", "Moving the folder to the Recycle Bin was cancelled.");
                return;
            }

            Profiles.Remove(profile.Id);
        }
        catch (Exception ex)
        {
            Log.Error($"Deleting profile {profile.Id} failed.", ex);
            ShowError(owner, "Could not delete the account", ex);
            return;
        }

        // The account is gone; what is left is tidying up, and none of it may undo that.
        var leftovers = new List<string>(shortcuts) { Paths.ProfileIconFile(profile.Id), Paths.ProfileBackupDir(profile.Id) };
        if (folder is not null && MsixVirtualization.GetPackageMirror(folder) is { } mirror)
        {
            leftovers.Add(mirror);
        }

        foreach (var item in leftovers)
        {
            try
            {
                RecycleBin.Move(item, ownerHandle);
            }
            catch (IOException ex)
            {
                Log.Warn($"Could not move a leftover of profile {profile.Id} to the Recycle Bin.", ex);
            }
        }

        try
        {
            State.Update(s => s.Without(profile.Id));
        }
        catch (IOException ex)
        {
            Log.Warn("Could not update the runtime state after deleting an account.", ex);
        }

        Notifier.Info("Account removed", $"“{profile.DisplayName}” was removed. Its folder is in the Recycle Bin.");
    }

    public async Task CopySetupAsync(Window? owner, Profile target)
    {
        var sources = Profiles.Profiles.Where(p => p.Id != target.Id).ToList();
        var dialog = new CopySetupDialog(owner, target, sources);
        if (dialog.ShowDialog() != true || dialog.SelectedSource is not { } source)
        {
            return;
        }

        if (!await EnsureClosedAsync(owner, target, "Close and copy"))
        {
            return;
        }

        try
        {
            SetupCopyReport report;
            var ownerHandle = OwnerHandle(owner);
            using (BusyCursor())
            {
                report = await Task.Run(() => ProfileSetup.Copy(source, target, ownerHandle));
            }

            MessageDialog.Show(
                owner,
                "Setup copied",
                $"“{target.DisplayName}” now has the app setup of “{source.DisplayName}”." +
                (report.ReplacedCount > 0 ? $" {report.ReplacedCount} existing item(s) went to the Recycle Bin." : string.Empty),
                details: report.Copied.Select(i => i.Description));
        }
        catch (Exception ex)
        {
            Log.Error($"Copying setup into profile {target.Id} failed.", ex);
            ShowError(owner, "Could not copy the setup", ex);
        }
    }

    public void CreateShortcut(Window? owner, Profile profile, ShortcutLocation location)
    {
        try
        {
            var path = Shortcuts.CreateForProfile(profile, location);
            Notifier.Info("Shortcut created", Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex)
        {
            ShowError(owner, "Could not create the shortcut", ex);
        }
    }

    public void OpenDataFolder(Profile profile)
    {
        var folder = profile.IsDefault
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude")
            : profile.DataFolder;
        OpenFolder(folder);
    }

    public static void OpenFolder(string? folder)
    {
        if (folder is not null && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
        }
    }

    // ------------------------------------------------------------------ settings

    public string ApplyHotkey()
    {
        var settings = Settings.Current;
        if (!settings.HotkeyEnabled)
        {
            Hotkey.Unregister();
            return "Off";
        }

        return Hotkey.TryRegister(settings.Hotkey) ? "Ready" : "Used by another app – pick another one";
    }

    public void SetStartup(Window? owner, bool enabled)
    {
        try
        {
            if (enabled)
            {
                Shortcuts.EnableStartup();
            }
            else if (Shortcuts.IsStartupEnabled)
            {
                Shortcuts.DisableStartup();
            }
        }
        catch (Exception ex)
        {
            ShowError(owner, "Could not change the startup setting", ex);
        }
    }

    // ------------------------------------------------------------------ sign-in helper

    /// <summary>The guided one-time setup. Returns whether sign-in links now reach Tandem.</summary>
    public bool EnableHelper(Window? owner, string? reason = null) => SignInSetupDialog.Run(owner, ExePath, reason);

    public void RepairHelper(Window? owner)
    {
        try
        {
            LinkHandlerRegistration.Register(ExePath);
        }
        catch (Exception ex)
        {
            ShowError(owner, "Could not repair the sign-in helper", ex);
        }
    }

    public void DisableHelper(Window? owner)
    {
        if (HelperStatus == LinkHandlerStatus.Active)
        {
            var choice = MessageDialog.Show(
                owner,
                "Give claude:// links back to Claude",
                "Windows only lets you change this yourself. In the Settings page that opens, choose Claude for CLAUDE links. " +
                "Tandem then removes its entries automatically.",
                "Open Windows Settings",
                "Cancel");
            if (choice != DialogChoice.Primary)
            {
                return;
            }

            OpenUri(LinkHandlerRegistration.SettingsUriForClaude);
            WatchHelper(until: LinkHandlerStatus.WaitingForUserChoice, onReached: () =>
            {
                LinkHandlerRegistration.Unregister();
                Notifier.Info("Sign-in helper is off", "claude:// links open in Claude again.");
            });
            return;
        }

        var turnOff = MessageDialog.Show(
            owner, "Turn off the sign-in helper?", "Tandem removes its own entries from your Windows user settings.", "Turn off", "Cancel");
        if (turnOff == DialogChoice.Primary)
        {
            try
            {
                LinkHandlerRegistration.Unregister();
            }
            catch (Exception ex)
            {
                ShowError(owner, "Could not turn off the sign-in helper", ex);
            }
        }
    }

    public static void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    // ------------------------------------------------------------------ lifetime

    public void Quit() => _app.Shutdown();

    public void Dispose()
    {
        _helperWatch?.Stop();
        Usage.Dispose();
        _menu?.Close();
        Hotkey.Dispose();
        Overlay.Dispose();
        Foreground.Dispose();
        Tray.Dispose();
        Theme.Dispose();
    }

    public static void ShowError(Window? owner, string title, Exception ex) =>
        MessageDialog.Show(owner, title, ex is ProfileValidationException or VirtualizedFolderException or ClaudeNotInstalledException or IOException
            ? ex.Message
            : "Something went wrong. Details are in Tandem's log file.\n\n" + ex.Message);

    /// <summary>
    /// Opens an account for signing in. The sign-in helper is set up first (once, guided), so the
    /// browser's "Open" always lands in this window and never in an account that is already
    /// signed in.
    /// </summary>
    private void BeginSignIn(Window? owner, Profile profile, bool isNew)
    {
        var helperOn = HelperStatus == LinkHandlerStatus.Active
            || EnableHelper(owner, isNew ? $"Before “{profile.DisplayName}” opens:" : null);

        try
        {
            Switcher.ExpectSignIn(profile);
        }
        catch (IOException ex)
        {
            Log.Warn("Could not record the pending sign-in.", ex);
        }

        Switcher.SwitchTo(profile, CurrentProfile, Foreground.CurrentWindow);

        if (helperOn)
        {
            Notifier.Info(
                $"Sign in to “{profile.DisplayName}”",
                "Use Google or email in the window that opens. When the browser asks to open the app, choose Open: the sign-in comes back to this window.");
        }
        else
        {
            Notifier.Warning(
                "Sign-in helper is not set up",
                "The browser will hand the sign-in to your original Claude, not to the new window. Set it up on the Sign-in page, then use Sign in again.");
        }
    }

    private async Task<bool> EnsureClosedAsync(Window? owner, Profile profile, string action)
    {
        var state = Instances.StateOf(profile);
        if (state == AccountRunState.NotRunning)
        {
            return true;
        }

        var message = state == AccountRunState.Background
            ? "Its window is closed, but Claude keeps running in the background (in the tray). Tandem can close it completely, like Quit in Claude's tray menu."
            : "Its Claude window is open. Tandem can close it completely, including the part that keeps running in the background.";
        message += " Anything still running inside this account, such as a Claude Code session, stops too. Your other accounts are not affected.";
        if (MessageDialog.Show(owner, $"“{profile.DisplayName}” is still running", message, action, "Cancel") != DialogChoice.Primary)
        {
            return false;
        }

        bool closed;
        using (BusyCursor())
        {
            closed = await Switcher.QuitCompletelyAsync(profile);
        }

        if (closed)
        {
            return true;
        }

        MessageDialog.Show(
            owner, "It is still running", $"Claude for “{profile.DisplayName}” did not close. Quit it from its icon in the tray, then try again.");
        return false;
    }

    private void OnActiveChanged()
    {
        if (Foreground.Active?.Profile is { } profile && profile.Id != _lastSavedActiveId)
        {
            _lastSavedActiveId = profile.Id;
            Switcher.RememberActive(profile);
        }

        Tray.Update(CurrentProfile);
    }

    /// <summary>
    /// Makes sure every added account has a working Claude Code program, also ones that are open
    /// already (the download can be refused on some networks). Runs in the background.
    /// </summary>
    private void ProvideClaudeCode()
    {
        var profiles = Profiles.Profiles.ToList();
        Task.Run(() =>
        {
            foreach (var profile in profiles.Where(p => !p.IsDefault))
            {
                try
                {
                    Tandem.Core.Claude.ClaudeCodeShare.Provide(profile, profiles);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Giving profile {profile.Id} Claude Code failed.", ex);
                }
            }
        });
    }

    /// <summary>
    /// When the account in use reaches a limit, says so once and names the account with the most
    /// room, so switching is an informed click away.
    /// </summary>
    private void OnUsageUpdated(Tandem.Core.Usage.UsageOverview previous)
    {
        try
        {
            if (Profiles.Profiles.Count < 2)
            {
                return;
            }

            var current = CurrentProfile;
            if (Usage.Current.For(current.Id) is not { LimitReached: true } report
                || previous.For(current.Id) is { LimitReached: true }
                || Instances.StateOf(current) != AccountRunState.Open)
            {
                return;
            }

            var key = $"{current.Id}:{report.AvailableAt?.ToUnixTimeSeconds()}";
            if (!_limitNotices.Add(key))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var others = Profiles.Profiles.Where(p => p.Id != current.Id).Select(p => p.Id);
            var pick = Tandem.Core.Usage.UsageReader.Recommend(Usage.Current, others);
            var how = Hotkey.Registered is { } hotkey ? $"the tray icon or {hotkey}" : "the tray icon";
            var text = pick is { AllLimited: false } && Profiles.Find(pick.ProfileId) is { } best && Usage.Current.For(best.Id) is { } bestReport
                ? $"“{best.DisplayName}” has the most room: {UsageText.Left(bestReport)} Switch from {how}."
                : "Your other accounts are at a limit too, or have no readings yet. See Tandem → Usage.";
            Notifier.Info($"“{current.DisplayName}”: {UsageText.LimitLine(report, now)}", text);
        }
        catch (Exception ex)
        {
            Log.Warn("Checking usage limits failed.", ex);
        }
    }

    private void WatchHelper(LinkHandlerStatus until, Action onReached)
    {
        _helperWatch?.Stop();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            if (HelperStatus == until)
            {
                timer.Stop();
                onReached();
            }
            else if (DateTime.UtcNow - started > TimeSpan.FromMinutes(5))
            {
                timer.Stop();
            }
        };
        _helperWatch = timer;
        timer.Start();
    }

    public RuntimeState SafeReadState()
    {
        try
        {
            return State.Read();
        }
        catch (IOException)
        {
            return new RuntimeState();
        }
    }

    private Dictionary<string, AccountRunState> SafeStates()
    {
        try
        {
            return Instances.States();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not list running instances.", ex);
            return new Dictionary<string, AccountRunState>();
        }
    }

    private IReadOnlyList<string> SafeFindShortcuts(Profile profile)
    {
        try
        {
            return Shortcuts.FindForProfile(profile.Id);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not look for shortcuts.", ex);
            return [];
        }
    }

    private static IDisposable BusyCursor()
    {
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.AppStarting;
        return new CursorReset();
    }

    private static IntPtr OwnerHandle(Window? owner) =>
        owner is { IsVisible: true } ? WindowTools.Handle(owner) : IntPtr.Zero;

    private sealed class CursorReset : IDisposable
    {
        public void Dispose() => System.Windows.Input.Mouse.OverrideCursor = null;
    }
}
