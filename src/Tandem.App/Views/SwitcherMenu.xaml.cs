using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Tandem.App.Native;
using Tandem.Core.Profiles;
using Tandem.Core.Usage;

namespace Tandem.App.Views;

public enum MenuPlacement
{
    /// <summary>Next to an anchor rectangle (the switch button), below it or above it.</summary>
    NextToAnchor,

    /// <summary>Next to a point, e.g. the cursor at the tray icon.</summary>
    AtPoint,

    /// <summary>Centered on a rectangle, e.g. the active Claude window.</summary>
    Centered,
}

/// <summary>The compact switcher: every account (current one marked), Add account…, Manage….</summary>
public partial class SwitcherMenu : Window
{
    private readonly List<(Button Button, Profile Profile)> _profileButtons = [];
    private readonly List<Button> _allButtons = [];
    private bool _closing;

    internal SwitcherMenu(
        IReadOnlyList<Profile> profiles,
        string? currentId,
        IReadOnlyDictionary<string, Services.AccountRunState> states,
        UsageOverview usage,
        bool includeQuit,
        string? hotkey)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(hotkey))
        {
            HotkeyHintText.Text = hotkey;
            HotkeyHint.Visibility = Visibility.Visible;
        }

        // Point out the account with the most room, when there is a real choice.
        var withData = profiles.Count(p => usage.For(p.Id) is not null);
        var pick = withData >= 2 ? UsageReader.Recommend(usage, profiles.Select(p => p.Id)) : null;
        var bestId = pick is { AllLimited: false } ? pick.ProfileId : null;
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var state = states.TryGetValue(profile.Id, out var s) ? s : Services.AccountRunState.NotRunning;
            var button = MakeProfileItem(profile, i, profile.Id == currentId, state, usage.For(profile.Id), profile.Id == bestId, now);
            button.Click += (_, _) => Choose(() => ProfileChosen?.Invoke(profile));
            _profileButtons.Add((button, profile));
            Items.Children.Add(button);
            _allButtons.Add(button);
        }

        if (profiles.Count > 1)
        {
            var open = states.Values.Count(v => v == Services.AccountRunState.Open);
            if (open < profiles.Count)
            {
                AddCommand("", "Open all accounts", () => OpenAllRequested?.Invoke());
            }

            if (open > 1)
            {
                AddCommand("", "Arrange windows side by side", () => ArrangeRequested?.Invoke());
            }
        }

        AddCommand("", "Add account…", () => AddRequested?.Invoke());
        AddCommand("", "Manage accounts…", () => ManageRequested?.Invoke());
        if (includeQuit)
        {
            AddCommand("", "Quit Tandem", () => QuitRequested?.Invoke());
        }

        Deactivated += (_, _) => CloseOnce();
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => WindowTools.AddExtendedStyle(this, User32.WS_EX_TOOLWINDOW);
    }

    public event Action<Profile>? ProfileChosen;

    public event Action? AddRequested;

    public event Action? OpenAllRequested;

    public event Action? ArrangeRequested;

    public event Action? ManageRequested;

    public event Action? QuitRequested;

    /// <summary>Shows and activates the menu; the anchor is in physical screen pixels.</summary>
    internal void ShowAt(MenuPlacement placement, User32.RECT anchor)
    {
        // Show off-screen first so the window exists, then move it to the target monitor
        // (which may change its DPI and size), measure, and place it for real.
        Show();
        var hwnd = WindowTools.Handle(this);
        WindowTools.MoveTopmost(hwnd, anchor.Left, anchor.Top);
        UpdateLayout();

        User32.GetWindowRect(hwnd, out var own);
        var work = WindowTools.GetWorkAreaAt(anchor.Left, anchor.Top);
        var shadow = (int)Math.Round(16 * WindowTools.GetScale(hwnd));
        var gap = (int)Math.Round(6 * WindowTools.GetScale(hwnd));
        var opensUp = false;
        int x, y;
        switch (placement)
        {
            case MenuPlacement.NextToAnchor:
                // Align the menu's edge with the button's nearer edge; open downwards when there is room.
                var alignRight = anchor.Left + (anchor.Width / 2) > (work.Left + work.Right) / 2;
                x = alignRight ? anchor.Right - own.Width + shadow : anchor.Left - shadow;
                y = anchor.Bottom - shadow + gap;
                if (y + own.Height - shadow > work.Bottom)
                {
                    y = anchor.Top - own.Height + shadow - gap;
                    opensUp = true;
                }

                break;
            case MenuPlacement.AtPoint:
                x = anchor.Left + own.Width > work.Right ? anchor.Left - own.Width + shadow : anchor.Left - shadow;
                opensUp = anchor.Top + own.Height > work.Bottom;
                y = opensUp ? anchor.Top - own.Height + shadow : anchor.Top - shadow;
                break;
            default:
                x = anchor.Left + ((anchor.Width - own.Width) / 2);
                y = anchor.Top + ((anchor.Height - own.Height) / 3);
                break;
        }

        x = Math.Clamp(x, work.Left - shadow, Math.Max(work.Left - shadow, work.Right - own.Width + shadow));
        y = Math.Clamp(y, work.Top - shadow, Math.Max(work.Top - shadow, work.Bottom - own.Height + shadow));
        WindowTools.MoveTopmost(hwnd, x, y);

        Card.RenderTransformOrigin = new Point(0.5, opensUp ? 1 : 0);
        Animate(opensUp);
        Activate();
        var focus = _profileButtons.FirstOrDefault(b => b.Button.Tag is true).Button ?? _allButtons.FirstOrDefault();
        focus?.Focus();
    }

    private void Animate(bool opensUp)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(170);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        if (Card.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform shift] })
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(opensUp ? 6 : -6, 0, duration) { EasingFunction = ease });
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseOnce();
                e.Handled = true;
                return;
            case Key.Down or Key.Up:
                MoveFocus(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                return;
        }

        var index = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (index >= 0 && index < _profileButtons.Count)
        {
            var profile = _profileButtons[index].Profile;
            Choose(() => ProfileChosen?.Invoke(profile));
            e.Handled = true;
        }
    }

    private void MoveFocus(int step)
    {
        if (_allButtons.Count == 0)
        {
            return;
        }

        var current = _allButtons.FindIndex(b => b.IsKeyboardFocused);
        var next = current < 0 ? 0 : (current + step + _allButtons.Count) % _allButtons.Count;
        _allButtons[next].Focus();
        _allButtons[next].BringIntoView();
    }

    private void Choose(Action action)
    {
        CloseOnce();
        Dispatcher.BeginInvoke(action);
    }

    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    private Button MakeProfileItem(
        Profile profile, int index, bool isCurrent, Services.AccountRunState state, UsageReport? usage, bool isBest, DateTimeOffset now)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar with the account's color and initial; a green dot when it is open.
        var avatar = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        avatar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = (Brush)new BrushConverter().ConvertFromString(profile.Color)!,
            Child = new TextBlock
            {
                Text = ProfileRow.InitialOf(profile.DisplayName),
                FontFamily = (FontFamily)FindResource("Font.Display"),
                FontSize = 15,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1),
            },
        });
        var running = state != Services.AccountRunState.NotRunning;
        var stateBrush = state == Services.AccountRunState.Open ? "Brush.Success" : "Brush.Warning";
        if (running)
        {
            var dot = new Ellipse
            {
                Width = 11,
                Height = 11,
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1),
            };
            dot.SetResourceReference(Shape.FillProperty, stateBrush);
            dot.SetResourceReference(Shape.StrokeProperty, "Brush.Menu");
            avatar.Children.Add(dot);
        }

        grid.Children.Add(avatar);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        var name = new TextBlock
        {
            Text = profile.DisplayName,
            FontSize = 13.5,
            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        text.Children.Add(name);
        var plan = ProfileRow.PlanName(profile.Plan);
        var status = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var limit = usage is null ? null : UsageText.ShortLimitLine(usage, now);
        if (limit is not null)
        {
            status.Text = limit;
            status.ToolTip = UsageText.LimitLine(usage!, now);
            status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        }
        else
        {
            status.Text = (isCurrent ? "Current" : ProfileRow.StatusOf(state)) + (plan.Length > 0 ? " · " + plan : string.Empty);
            status.SetResourceReference(TextBlock.ForegroundProperty, isCurrent ? "Brush.AccentText" : running ? stateBrush : "Brush.TextSubtle");
            if (isBest)
            {
                var best = new System.Windows.Documents.Run(" · Most room");
                best.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Success");
                status.Inlines.Add(best);
            }
        }

        text.Children.Add(status);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (usage is not null)
        {
            right.Children.Add(MakeMeters(usage, now));
        }
        if (isCurrent)
        {
            var check = new TextBlock { Text = "", Style = (Style)FindResource("Text.Icon"), FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
            check.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
            right.Children.Add(check);
        }

        if (index < 9)
        {
            var number = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            number.SetResourceReference(Border.BackgroundProperty, "Brush.Badge");
            ((TextBlock)number.Child).SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            right.Children.Add(number);
        }

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var button = new Button
        {
            Style = (Style)FindResource("Button.MenuItem"),
            Content = grid,
            Tag = isCurrent,
            Padding = new Thickness(8, 7, 8, 7),
        };
        if (isCurrent)
        {
            button.SetResourceReference(BackgroundProperty, "Brush.AccentSoft");
        }

        System.Windows.Automation.AutomationProperties.SetName(button, $"{profile.DisplayName}{(isCurrent ? ", current" : string.Empty)}");
        return button;
    }

    /// <summary>Two small meters, 5-hour over weekly, with the percentages; details in the tooltip.</summary>
    private FrameworkElement MakeMeters(UsageReport usage, DateTimeOffset now)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Background = Brushes.Transparent };
        var tip = new List<string>();
        foreach (var limit in new[] { LimitView.FiveHour(usage, now), LimitView.Weekly(usage, now) })
        {
            if (limit is null)
            {
                continue;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var bar = new Controls.UsageBar { Width = 30, Height = 4, Value = limit.Value, VerticalAlignment = VerticalAlignment.Center };
            bar.SetResourceReference(Controls.UsageBar.TrackProperty, "Brush.Badge");
            bar.SetResourceReference(Controls.UsageBar.FillProperty, limit.Level switch
            {
                UsageLevel.Low => "Brush.Success",
                UsageLevel.Medium => "Brush.Warning",
                _ => "Brush.Danger",
            });
            row.Children.Add(bar);
            var value = new TextBlock
            {
                Text = limit.PercentText,
                FontSize = 10.5,
                Width = 30,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            row.Children.Add(value);
            panel.Children.Add(row);
            tip.Add($"{limit.Title}: {limit.PercentText} used. {limit.Detail}.");
        }

        panel.ToolTip = string.Join("\n", tip);
        return panel;
    }

    private void AddCommand(string glyph, string label, Action action)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Text.Icon"), FontSize = 13, Width = 32 };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        panel.Children.Add(icon);
        var text = new TextBlock { Text = label, FontSize = 13.5 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        panel.Children.Add(text);
        var button = new Button { Style = (Style)FindResource("Button.MenuItem"), Content = panel, Padding = new Thickness(8, 8, 8, 8) };
        button.Click += (_, _) => Choose(action);
        Commands.Children.Add(button);
        _allButtons.Add(button);
    }
}
