using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tandem.Core.Integration;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;
using Tandem.Core.Routing;
using Tandem.Core.Settings;
using Tandem.Core.Shell;
using Tandem.Core.Usage;

namespace Tandem.App.Views;

/// <summary>Accounts and settings, in a sidebar layout.</summary>
public partial class ManageWindow : Window
{
    private readonly AppHost _host;
    private readonly ObservableCollection<ProfileRow> _rows = [];
    private readonly ObservableCollection<UsageRow> _usageRows = [];
    private readonly DispatcherTimer _timer;
    private string? _bestId;
    private bool _loading;

    internal ManageWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        host.Theme.Attach(this);
        ProfileList.ItemsSource = _rows;
        UsageList.ItemsSource = _usageRows;
        VersionText.Text = $"Version {AppHost.AppVersion}";
        SidebarVersion.Text = $"Version {AppHost.AppVersion}  ·  by {AppHost.AuthorName}";

        LoadSettings();
        RebuildRows();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshStatus();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Activated += (_, _) => RefreshStatus();

        host.Profiles.Changed += OnProfilesChanged;
        host.Settings.Changed += OnSettingsChanged;
        host.Usage.Updated += OnUsageUpdated;
        Closed += (_, _) =>
        {
            _timer.Stop();
            host.Profiles.Changed -= OnProfilesChanged;
            host.Settings.Changed -= OnSettingsChanged;
            host.Usage.Updated -= OnUsageUpdated;
        };

        HotkeyInput.GestureChanged += (_, gesture) => Safe(() =>
        {
            _host.Settings.Update(s => s.Hotkey = gesture);
            ShowHotkeyStatus(_host.ApplyHotkey());
        });
    }

    /// <summary>Shows one page: "accounts", "usage", "switching", "signin", "general" or "about".</summary>
    internal void ShowPage(string page)
    {
        var nav = page switch
        {
            "usage" => NavUsage,
            "switching" => NavSwitching,
            "signin" => NavSignIn,
            "general" => NavGeneral,
            "about" => NavAbout,
            _ => NavAccounts,
        };
        nav.IsChecked = true;
        OnNav(nav, new RoutedEventArgs());
    }

    /// <summary>For screenshots of a page's lower part.</summary>
    internal void ScrollToEnd() => Scroller.ScrollToEnd();

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (PageAccounts is null)
        {
            return;
        }

        var page = sender == NavUsage ? PageUsage
            : sender == NavSwitching ? PageSwitching
            : sender == NavSignIn ? PageSignIn
            : sender == NavGeneral ? PageGeneral
            : sender == NavAbout ? PageAbout
            : PageAccounts;
        foreach (var child in new UIElement[] { PageAccounts, PageUsage, PageSwitching, PageSignIn, PageGeneral, PageAbout })
        {
            child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
        }

        if (page == PageUsage)
        {
            _host.Usage.Refresh(TimeSpan.FromSeconds(5));
        }

        Scroller.ScrollToTop();
        AnimateIn();
    }

    /// <summary>A short fade and rise, so changing pages feels smooth.</summary>
    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Pages.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        if (Pages.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }
    }

    private void OnProfilesChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RebuildRows);

    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(LoadSettings);

    private void LoadSettings()
    {
        _loading = true;
        var settings = _host.Settings.Current;

        SamePlaceToggle.IsChecked = settings.OpenInSamePlace;
        LeaveOpenRadio.IsChecked = settings.SwitchBehavior == SwitchBehavior.LeaveOpen;
        MinimizeRadio.IsChecked = settings.SwitchBehavior == SwitchBehavior.Minimize;
        CloseRadio.IsChecked = settings.SwitchBehavior == SwitchBehavior.Close;

        OverlayToggle.IsChecked = settings.OverlayEnabled;
        ScopeFront.IsChecked = settings.OverlayScope == OverlayScope.FrontWindow;
        ScopeEvery.IsChecked = settings.OverlayScope == OverlayScope.EveryWindow;
        CornerTopRight.IsChecked = settings.OverlayCorner == OverlayCorner.TopRight;
        CornerTopLeft.IsChecked = settings.OverlayCorner == OverlayCorner.TopLeft;
        CornerBottomRight.IsChecked = settings.OverlayCorner == OverlayCorner.BottomRight;
        CornerBottomLeft.IsChecked = settings.OverlayCorner == OverlayCorner.BottomLeft;

        HotkeyToggle.IsChecked = settings.HotkeyEnabled;
        HotkeyInput.Gesture = settings.Hotkey;
        HotkeyInput.IsEnabled = settings.HotkeyEnabled;
        ShowHotkeyStatus(!settings.HotkeyEnabled ? "Off"
            : _host.Hotkey.Registered == settings.Hotkey ? "Ready" : "Used by another app");

        FallbackLastActive.IsChecked = settings.LinkFallback == LinkFallback.LastActive;
        FallbackDefault.IsChecked = settings.LinkFallback == LinkFallback.Default;

        SyncSetupToggle.IsChecked = settings.SyncSetupFromOriginal;
        ThemeSystem.IsChecked = settings.Theme == ThemeMode.System;
        ThemeLight.IsChecked = settings.Theme == ThemeMode.Light;
        ThemeDark.IsChecked = settings.Theme == ThemeMode.Dark;

        StartupToggle.IsChecked = _host.Shortcuts.IsStartupEnabled;
        var hasShortcut = _host.Shortcuts.HasAppShortcut;
        StartMenuButton.Visibility = hasShortcut ? Visibility.Collapsed : Visibility.Visible;
        StartMenuText.Text = hasShortcut
            ? "Tandem is in the Start menu. Start it from there."
            : "Start Tandem from the Start menu or Explorer, not from a terminal inside Claude.";
        _loading = false;
        UpdateOnboarding();
    }

    private void ShowHotkeyStatus(string status)
    {
        HotkeyStatus.Text = status;
        HotkeyChip.Style = (Style)FindResource(status == "Ready" ? "Chip.Success" : "Chip");
    }

    private void RebuildRows()
    {
        _rows.Clear();
        _usageRows.Clear();
        foreach (var profile in _host.Profiles.Profiles)
        {
            _rows.Add(new ProfileRow(profile));
            _usageRows.Add(new UsageRow(profile));
        }

        RefreshStatus();
    }

    private void OnUsageUpdated(UsageOverview previous) => RefreshStatus();

    private void RefreshStatus()
    {
        try
        {
            var states = _host.Instances.States();
            var current = _host.CurrentProfile.Id;
            var state = _host.SafeReadState();
            foreach (var row in _rows)
            {
                row.RunState = states.TryGetValue(row.Profile.Id, out var s) ? s : Services.AccountRunState.NotRunning;
                row.IsCurrent = row.Profile.Id == current;
                row.LastUsed = state.LastUsedOf(row.Profile.Id);
            }

            var open = states.Values.Count(s => s == Services.AccountRunState.Open);
            OpenAllButton.IsEnabled = open < _rows.Count;
            ArrangeButton.IsEnabled = open > 1;

            RefreshUsage(states, current);
            _host.Usage.Refresh(TimeSpan.FromSeconds(PageUsage.IsVisible ? 15 : 45));

            ClaudeMissingNotice.Visibility = AppHost.IsClaudeInstalled ? Visibility.Collapsed : Visibility.Visible;
            UpdateHelper(_host.HelperStatus);
            UpdateOnboarding();
        }
        catch (Exception ex)
        {
            Log.Warn("Refreshing the Manage window failed.", ex);
        }
    }

    // ------------------------------------------------------------------ usage

    private void RefreshUsage(IReadOnlyDictionary<string, Services.AccountRunState> states, string currentId)
    {
        var usage = _host.Usage.Current;
        var now = DateTimeOffset.UtcNow;
        var names = _host.Profiles.Profiles.ToDictionary(p => p.Id, p => p.DisplayName);
        var withData = _host.Profiles.Profiles.Count(p => usage.For(p.Id) is not null);
        var pick = withData >= 2 ? UsageReader.Recommend(usage, names.Keys) : null;
        var best = pick is { AllLimited: false } ? pick.ProfileId : null;

        foreach (var row in _rows)
        {
            row.SetUsage(usage.For(row.Profile.Id), now);
        }

        foreach (var row in _usageRows)
        {
            var id = row.Profile.Id;
            var group = usage.SameAccount.FirstOrDefault(g => g.Contains(id));
            var others = group?.Where(other => other != id && names.ContainsKey(other)).Select(other => $"“{names[other]}”").ToList();
            row.Update(
                usage.For(id),
                states.TryGetValue(id, out var s) ? s : Services.AccountRunState.NotRunning,
                id == currentId,
                id == best,
                others is { Count: > 0 } ? string.Join(" and ", others) : null,
                now);
        }

        UpdateBestCard(pick, usage, states, currentId, now);

        var sameNames = usage.SameAccount
            .Select(g => g.Where(names.ContainsKey).Select(id => $"“{names[id]}”").ToList())
            .Where(g => g.Count > 1)
            .Select(g => string.Join(" and ", g))
            .ToList();
        SameAccountNotice.Visibility = sameNames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SameAccountText.Text = sameNames.Count == 0 ? string.Empty
            : $"{string.Join("; ", sameNames)} are signed in to the same Claude account, so they share one set of limits. "
              + "To use a different account there, choose Sign in again on its card and sign in with another login.";
    }

    private void UpdateBestCard(
        UsageRecommendation? pick,
        UsageOverview usage,
        IReadOnlyDictionary<string, Services.AccountRunState> states,
        string currentId,
        DateTimeOffset now)
    {
        var profile = pick is null ? null : _host.Profiles.Find(pick.ProfileId);
        var report = profile is null ? null : usage.For(profile.Id);
        if (pick is null || profile is null || report is null)
        {
            BestCard.Visibility = Visibility.Collapsed;
            _bestId = null;
            return;
        }

        _bestId = profile.Id;
        var state = states.TryGetValue(profile.Id, out var s) ? s : Services.AccountRunState.NotRunning;
        BestCard.Visibility = Visibility.Visible;
        BestAvatar.Background = (Brush)new BrushConverter().ConvertFromString(profile.Color)!;
        BestInitial.Text = ProfileRow.InitialOf(profile.DisplayName);
        BestTitle.Text = profile.DisplayName;
        if (pick.AllLimited)
        {
            BestCard.SetResourceReference(Border.BackgroundProperty, "Brush.Notice");
            BestCaption.Text = "EVERY ACCOUNT IS AT A LIMIT";
            BestText.Text = pick.AvailableAt is { } at
                ? $"This one is back first: by {UsageText.When(at, now)}, in {UsageText.In(at - now)} at most."
                : "None of them shows yet when it is back.";
        }
        else
        {
            BestCard.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            BestCaption.Text = "MOST ROOM RIGHT NOW";
            BestText.Text = UsageText.Left(report);
        }

        var inUse = profile.Id == currentId && state == Services.AccountRunState.Open;
        BestInUse.Visibility = inUse ? Visibility.Visible : Visibility.Collapsed;
        BestButton.Visibility = inUse ? Visibility.Collapsed : Visibility.Visible;
        BestButton.Content = state switch
        {
            Services.AccountRunState.Open => "Switch",
            Services.AccountRunState.Background => "Show",
            _ => "Open",
        };
        BestButton.Style = (Style)FindResource(pick.AllLimited ? "Button.Secondary" : "Button.Primary");
    }

    private void OnUseBest(object sender, RoutedEventArgs e)
    {
        if (_bestId is not null && _host.Profiles.Find(_bestId) is { } profile)
        {
            Safe(() => _host.SwitchTo(profile));
        }
    }

    private void OnRefreshUsage(object sender, RoutedEventArgs e) => Safe(() =>
    {
        _host.Usage.ReadNow(TimeSpan.Zero);
        RefreshStatus();
    });

    private void UpdateOnboarding()
    {
        if (Onboarding is null)
        {
            return;
        }

        var hasSecond = _rows.Count >= 2;
        var helperOn = _host.HelperStatus == LinkHandlerStatus.Active;
        SetStep(Step1Circle, Step1Mark, Step1Button, hasSecond, "1");
        SetStep(Step2Circle, Step2Mark, Step2Button, helperOn, "2");
        Onboarding.Visibility = _host.Settings.Current.OnboardingDismissed || (hasSecond && helperOn)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetStep(Border circle, TextBlock mark, Button button, bool done, string number)
    {
        mark.Text = done ? "" : number;
        mark.FontFamily = (FontFamily)FindResource(done ? "Font.Icons" : "Font.UI");
        circle.SetResourceReference(Border.BackgroundProperty, done ? "Brush.Accent" : "Brush.Badge");
        mark.SetResourceReference(TextBlock.ForegroundProperty, done ? "Brush.OnAccent" : "Brush.TextMuted");
        button.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateHelper(LinkHandlerStatus status)
    {
        var fallback = _host.Settings.Current.LinkFallback == LinkFallback.LastActive ? "the account you used last" : "your original account";
        (string title, string text, string icon, string iconBack, string iconFore, string button, bool primary, bool showSettings) = status switch
        {
            LinkHandlerStatus.Active => (
                "The sign-in helper is on",
                $"Sign-ins from your browser go to the account that is signing in. Other claude:// links go to {fallback}.",
                "", "Brush.SuccessSoft", "Brush.Success", "Turn off…", false, false),
            LinkHandlerStatus.WaitingForUserChoice => (
                "One step left",
                "In Windows Settings, choose Tandem for CLAUDE links. Until then, sign-ins still go to your original account.",
                "", "Brush.Notice", "Brush.Warning", "Turn off", false, true),
            LinkHandlerStatus.NeedsRepair => (
                "Needs a quick repair",
                "Tandem.exe was moved, so the helper still points at its old location.",
                "", "Brush.DangerSurface", "Brush.Danger", "Repair", true, false),
            _ => (
                "The sign-in helper is off",
                "Without it, signing in to an added account ends up in your original Claude and the new window stays signed out.",
                "", "Brush.AccentSoft", "Brush.AccentText", "Turn on…", true, false),
        };

        HelperTitle.Text = title;
        HelperText.Text = text;
        HelperIcon.Text = icon;
        HelperIconBox.SetResourceReference(Border.BackgroundProperty, iconBack);
        HelperIcon.SetResourceReference(TextBlock.ForegroundProperty, iconFore);
        HelperButton.Content = button;
        HelperButton.Tag = status;
        HelperButton.Style = (Style)FindResource(primary ? "Button.Primary" : "Button.Secondary");
        HelperSettingsButton.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        Step2Button.Tag = status;
    }

    // ------------------------------------------------------------------ accounts

    private static Profile? RowProfile(object sender) => (sender as FrameworkElement)?.DataContext switch
    {
        ProfileRow row => row.Profile,
        UsageRow row => row.Profile,
        _ => null,
    };

    private void OnAddAccount(object sender, RoutedEventArgs e) => Safe(() => _host.AddAccount(this));

    private void OnOpenAll(object sender, RoutedEventArgs e) => Safe(() => _ = _host.OpenAllAsync());

    private void OnArrange(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ArrangeButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Item("", "Side by side on this screen", () => _host.ArrangeWindows(acrossScreens: false)));
        menu.Items.Add(Item("", "Spread across all screens", () => _host.ArrangeWindows(acrossScreens: true)));
        menu.IsOpen = true;
    }

    private void OnSwitch(object sender, RoutedEventArgs e)
    {
        if (RowProfile(sender) is { } profile)
        {
            Safe(() => _host.SwitchTo(profile));
        }
    }

    private void OnMore(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || RowProfile(sender) is not { } profile)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Item("", "Name, color and subscription…", () => _host.EditAccount(this, profile)));
        menu.Items.Add(Item("", "Sign in again", () => _host.SignInAgain(this, profile)));
        if (!profile.IsDefault)
        {
            menu.Items.Add(Item("", "Bring Code history from your original Claude…", () => _ = _host.ImportHistoryAsync(this, profile)));
            menu.Items.Add(Item("", "Copy app setup from another account…", () => _ = _host.CopySetupAsync(this, profile)));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("", "Create desktop shortcut", () => _host.CreateShortcut(this, profile, ShortcutLocation.Desktop)));
        menu.Items.Add(Item("", "Add to Start menu", () => _host.CreateShortcut(this, profile, ShortcutLocation.StartMenu)));
        menu.Items.Add(Item("", "Open data folder", () => _host.OpenDataFolder(profile)));
        if (!profile.IsDefault)
        {
            menu.Items.Add(new Separator());
            var delete = Item("", "Delete…", () => _ = _host.DeleteAccountAsync(this, profile));
            delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
            menu.Items.Add(delete);
        }

        menu.IsOpen = true;
    }

    private MenuItem Item(string glyph, string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Text.Icon"), FontSize = 13 },
        };
        item.Click += (_, _) => Safe(action);
        return item;
    }

    private void OnDismissOnboarding(object sender, RoutedEventArgs e) =>
        Safe(() => _host.Settings.Update(s => s.OnboardingDismissed = true));

    // ------------------------------------------------------------------ sign-in helper

    private void OnHelperButton(object sender, RoutedEventArgs e) => Safe(() =>
    {
        switch (_host.HelperStatus)
        {
            case LinkHandlerStatus.Off:
                _host.EnableHelper(this);
                break;
            case LinkHandlerStatus.NeedsRepair:
                _host.RepairHelper(this);
                break;
            default:
                if (sender == Step2Button)
                {
                    _host.EnableHelper(this);
                }
                else
                {
                    _host.DisableHelper(this);
                }

                break;
        }

        RefreshStatus();
    });

    private void OnHelperSettings(object sender, RoutedEventArgs e) => Safe(() =>
    {
        _host.EnableHelper(this);
        RefreshStatus();
    });

    private void OnFallbackChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var fallback = FallbackDefault.IsChecked == true ? LinkFallback.Default : LinkFallback.LastActive;
        Safe(() => _host.Settings.Update(s => s.LinkFallback = fallback));
    }

    // ------------------------------------------------------------------ switching

    private void OnSamePlaceToggle(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            var enabled = SamePlaceToggle.IsChecked == true;
            Safe(() => _host.Settings.Update(s => s.OpenInSamePlace = enabled));
        }
    }

    private void OnLeaveChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var behavior = MinimizeRadio.IsChecked == true ? SwitchBehavior.Minimize
            : CloseRadio.IsChecked == true ? SwitchBehavior.Close
            : SwitchBehavior.LeaveOpen;
        Safe(() => _host.Settings.Update(s => s.SwitchBehavior = behavior));
    }

    private void OnOverlayToggle(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            var enabled = OverlayToggle.IsChecked == true;
            Safe(() => _host.Settings.Update(s => s.OverlayEnabled = enabled));
        }
    }

    private void OnScopeChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            var scope = sender == ScopeEvery ? OverlayScope.EveryWindow : OverlayScope.FrontWindow;
            Safe(() => _host.Settings.Update(s => s.OverlayScope = scope));
        }
    }

    private void OnCornerChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var corner = sender == CornerTopLeft ? OverlayCorner.TopLeft
            : sender == CornerBottomRight ? OverlayCorner.BottomRight
            : sender == CornerBottomLeft ? OverlayCorner.BottomLeft
            : OverlayCorner.TopRight;
        Safe(() => _host.Settings.Update(s => s.ResetOverlay(corner)));
    }

    private void OnHotkeyToggle(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = HotkeyToggle.IsChecked == true;
        Safe(() =>
        {
            _host.Settings.Update(s => s.HotkeyEnabled = enabled);
            ShowHotkeyStatus(_host.ApplyHotkey());
        });
    }

    // ------------------------------------------------------------------ general

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var theme = sender == ThemeLight ? ThemeMode.Light : sender == ThemeDark ? ThemeMode.Dark : ThemeMode.System;
        Safe(() => _host.Settings.Update(s => s.Theme = theme));
    }

    private void OnSyncSetupToggle(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            var enabled = SyncSetupToggle.IsChecked == true;
            Safe(() => _host.Settings.Update(s => s.SyncSetupFromOriginal = enabled));
        }
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            var enabled = StartupToggle.IsChecked == true;
            Safe(() => _host.SetStartup(this, enabled));
        }
    }

    private void OnAddAppShortcut(object sender, RoutedEventArgs e) => Safe(() =>
    {
        _host.Shortcuts.CreateAppShortcut();
        LoadSettings();
    });

    private void OnOpenLogs(object sender, RoutedEventArgs e) => Safe(() => AppHost.OpenFolder(_host.Paths.LogsDir));

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => Safe(() => AppHost.OpenFolder(_host.Paths.LocalDir));

    /// <summary>Opens the project page in the browser; Tandem itself makes no network connection.</summary>
    private void OnProjectPage(object sender, RoutedEventArgs e) => Safe(() => AppHost.OpenUri(AppHost.ProjectUrl));

    private void OnAuthorPage(object sender, RoutedEventArgs e) => Safe(() => AppHost.OpenUri(AppHost.AuthorUrl));

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("A Manage window action failed.", ex);
            AppHost.ShowError(this, "Something went wrong", ex);
        }
    }
}
