using System.Windows.Threading;
using Tandem.App.Native;
using Tandem.App.Views;
using Tandem.Core.Profiles;
using Tandem.Core.Settings;

namespace Tandem.App.Services;

/// <summary>
/// The optional account button on Claude windows: on the window in front only (kept on top), or
/// on every open Claude window (each button stacked right above its own window, so other apps
/// still cover it normally). Attached to a window corner; follows moves, resizes and minimizing.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private readonly ForegroundTracker _foreground;
    private readonly InstanceTracker _instances;
    private readonly SettingsStore _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Action<OverlayWindow> _openMenu;
    private readonly Dictionary<IntPtr, OverlayWindow> _pills = [];
    private readonly Dictionary<uint, List<WinEventHook>> _processHooks = [];
    private readonly DispatcherTimer _refresh;
    private bool _updateQueued;

    public OverlayController(
        ForegroundTracker foreground, InstanceTracker instances, SettingsStore settings, Dispatcher dispatcher, Action<OverlayWindow> openMenu)
    {
        _foreground = foreground;
        _instances = instances;
        _settings = settings;
        _dispatcher = dispatcher;
        _openMenu = openMenu;
        _refresh = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, _) => Update();
        _foreground.ActiveChanged += (_, _) => Update();
        _foreground.ActiveMoved += (_, _) =>
        {
            if (_foreground.Active is { } active)
            {
                Reposition(active.Hwnd);
            }
        };
        _settings.Changed += (_, _) => Update();
    }

    private bool EveryWindow => _settings.Current.OverlayScope == OverlayScope.EveryWindow;

    /// <summary>Screen rectangle of a visible button, for anchoring the menu.</summary>
    public User32.RECT BoundsOf(OverlayWindow pill)
    {
        User32.GetWindowRect(pill.Hwnd, out var rect);
        var margin = Margin(pill);
        return new User32.RECT { Left = rect.Left + margin, Top = rect.Top + margin, Right = rect.Right - margin, Bottom = rect.Bottom - margin };
    }

    public void Update()
    {
        var settings = _settings.Current;
        var targets = new Dictionary<IntPtr, Profile?>();
        if (settings.OverlayEnabled)
        {
            if (settings.OverlayScope == OverlayScope.EveryWindow)
            {
                foreach (var instance in SafeSnapshot())
                {
                    var hwnd = WindowTools.FindMainWindow(instance.ProcessId);
                    if (IsShowable(hwnd))
                    {
                        targets[hwnd] = instance.Profile;
                    }
                }
            }
            else if (_foreground.Active is { } active && IsShowable(active.Hwnd))
            {
                targets[active.Hwnd] = active.Profile;
            }
        }

        foreach (var gone in _pills.Keys.Where(h => !targets.ContainsKey(h)).ToList())
        {
            _pills[gone].Close();
            _pills.Remove(gone);
        }

        foreach (var (hwnd, profile) in targets)
        {
            if (!_pills.TryGetValue(hwnd, out var pill))
            {
                pill = CreatePill(hwnd);
                _pills[hwnd] = pill;
            }

            pill.Topmost = !EveryWindow;
            pill.SetProfile(profile?.DisplayName ?? "Unknown account", profile?.Color ?? "#9C978B");
            if (!pill.IsVisible)
            {
                pill.Show();
            }

            pill.UpdateLayout();
            Reposition(hwnd);
        }

        UpdateWatchers(settings.OverlayEnabled && settings.OverlayScope == OverlayScope.EveryWindow);
    }

    public void Dispose()
    {
        _refresh.Stop();
        UpdateWatchers(false);
        foreach (var pill in _pills.Values)
        {
            pill.Close();
        }

        _pills.Clear();
    }

    private static bool IsShowable(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && User32.IsWindowVisible(hwnd) && !User32.IsIconic(hwnd) && !WindowTools.IsCloaked(hwnd);

    private static int Margin(OverlayWindow window) =>
        (int)Math.Round(OverlayWindow.ShadowMargin * WindowTools.GetScale(window.Hwnd));

    private static PixelRect ToPixelRect(User32.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private OverlayWindow CreatePill(IntPtr claudeWindow)
    {
        var pill = new OverlayWindow();
        pill.Clicked += (_, _) => _openMenu(pill);
        pill.Moved += (_, rect) => SaveAnchor(claudeWindow, pill, rect);
        pill.DpiChanged += (_, _) => _dispatcher.BeginInvoke(DispatcherPriority.Render, () => Reposition(claudeWindow));
        return pill;
    }

    private void Reposition(IntPtr claudeWindow)
    {
        if (!_pills.TryGetValue(claudeWindow, out var pill) || !pill.IsVisible)
        {
            return;
        }

        if (!IsShowable(claudeWindow))
        {
            pill.Hide();
            QueueUpdate();
            return;
        }

        var frame = ToPixelRect(WindowTools.GetFrameBounds(claudeWindow));
        var scale = WindowTools.GetScale(claudeWindow);
        User32.GetWindowRect(pill.Hwnd, out var own);
        var margin = Margin(pill);
        var settings = _settings.Current;
        var (x, y) = OverlayGeometry.Place(
            frame, own.Width - (2 * margin), own.Height - (2 * margin),
            settings.OverlayCorner, settings.OverlayOffsetX, settings.OverlayOffsetY, scale);

        if (EveryWindow)
        {
            PlaceJustAbove(pill.Hwnd, claudeWindow, x - margin, y - margin);
        }
        else
        {
            WindowTools.MoveTopmost(pill.Hwnd, x - margin, y - margin);
        }
    }

    /// <summary>Stacks the button directly above its Claude window, without making it topmost.</summary>
    private static void PlaceJustAbove(IntPtr pill, IntPtr claudeWindow, int x, int y)
    {
        const uint flags = User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER | User32.SWP_SHOWWINDOW;
        var above = User32.GetWindow(claudeWindow, User32.GW_HWNDPREV);
        if (above == pill)
        {
            User32.SetWindowPos(pill, IntPtr.Zero, x, y, 0, 0, flags | User32.SWP_NOZORDER);
            return;
        }

        var exStyle = above == IntPtr.Zero ? 0 : (long)User32.GetWindowLongPtr(above, User32.GWL_EXSTYLE);
        var insertAfter = above == IntPtr.Zero || (exStyle & User32.WS_EX_TOPMOST) != 0 ? User32.HWND_TOP : above;
        User32.SetWindowPos(pill, insertAfter, x, y, 0, 0, flags);
    }

    /// <summary>After a drag: attach the buttons to the nearest corner of the window.</summary>
    private void SaveAnchor(IntPtr claudeWindow, OverlayWindow pill, User32.RECT windowRect)
    {
        if (!User32.IsWindow(claudeWindow))
        {
            return;
        }

        var frame = ToPixelRect(WindowTools.GetFrameBounds(claudeWindow));
        var margin = Margin(pill);
        var bounds = new PixelRect(windowRect.Left + margin, windowRect.Top + margin, windowRect.Right - margin, windowRect.Bottom - margin);
        var (corner, x, y) = OverlayGeometry.Anchor(frame, bounds, WindowTools.GetScale(claudeWindow));
        _settings.Update(s =>
        {
            s.OverlayCorner = corner;
            s.OverlayOffsetX = x;
            s.OverlayOffsetY = y;
        });
    }

    /// <summary>In every-window mode: follow each Claude process's windows and re-check every second.</summary>
    private void UpdateWatchers(bool active)
    {
        if (!active)
        {
            _refresh.Stop();
            foreach (var hooks in _processHooks.Values)
            {
                hooks.ForEach(h => h.Dispose());
            }

            _processHooks.Clear();
            return;
        }

        _refresh.Start();
        var pids = _pills.Keys.Select(h => (uint)WindowTools.GetProcessId(h)).ToHashSet();
        foreach (var pid in _processHooks.Keys.Where(p => !pids.Contains(p)).ToList())
        {
            _processHooks[pid].ForEach(h => h.Dispose());
            _processHooks.Remove(pid);
        }

        foreach (var pid in pids.Where(p => !_processHooks.ContainsKey(p)))
        {
            _processHooks[pid] =
            [
                new WinEventHook(User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE, OnWindowEvent, pid),
                new WinEventHook(User32.EVENT_OBJECT_DESTROY, User32.EVENT_OBJECT_HIDE, OnWindowEvent, pid),
                new WinEventHook(User32.EVENT_OBJECT_CLOAKED, User32.EVENT_OBJECT_UNCLOAKED, OnWindowEvent, pid),
                new WinEventHook(User32.EVENT_SYSTEM_MINIMIZESTART, User32.EVENT_SYSTEM_MINIMIZEEND, OnWindowEvent, pid),
            ];
        }
    }

    private void OnWindowEvent(uint eventType, IntPtr hwnd, int idObject, int idChild)
    {
        if (idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF)
        {
            return;
        }

        if (eventType == User32.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (_pills.ContainsKey(hwnd))
            {
                Reposition(hwnd);
            }

            return;
        }

        QueueUpdate();
    }

    private void QueueUpdate()
    {
        if (_updateQueued)
        {
            return;
        }

        _updateQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _updateQueued = false;
            Update();
        });
    }

    private IReadOnlyList<RunningInstance> SafeSnapshot()
    {
        try
        {
            return _instances.Snapshot();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
