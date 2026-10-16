using System.Windows.Threading;
using Tandem.App.Native;
using Tandem.Core.Profiles;

namespace Tandem.App.Services;

/// <summary>The Claude window currently in the foreground.</summary>
internal sealed record ActiveClaudeWindow(IntPtr Hwnd, RunningInstance Instance)
{
    public Profile? Profile => Instance.Profile;
}

/// <summary>
/// Watches which window is in the foreground (out-of-context WinEvent hooks) and whether it is a
/// Claude window. Also follows that window's moves, resizes, minimize/restore and close.
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    private readonly InstanceTracker _instances;
    private readonly Dispatcher _dispatcher;
    private readonly WinEventHook _foregroundHook;
    private readonly WinEventHook _minimizeHook;
    private readonly List<WinEventHook> _windowHooks = [];
    private uint _hookedProcessId;
    private IntPtr _lastWindow;

    public ForegroundTracker(InstanceTracker instances, Dispatcher dispatcher)
    {
        _instances = instances;
        _dispatcher = dispatcher;
        _foregroundHook = new WinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, OnSystemEvent);
        _minimizeHook = new WinEventHook(User32.EVENT_SYSTEM_MINIMIZESTART, User32.EVENT_SYSTEM_MINIMIZEEND, OnSystemEvent);
    }

    /// <summary>Raised when the foreground Claude window changes (or there is none any more).</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Raised when the active Claude window moved or resized.</summary>
    public event EventHandler? ActiveMoved;

    /// <summary>The Claude window in the foreground, or null when something else is.</summary>
    public ActiveClaudeWindow? Active { get; private set; }

    /// <summary>The profile of the Claude window that was in the foreground most recently.</summary>
    public Profile? LastProfile { get; private set; }

    public bool IsActiveMinimized => Active is { } a && User32.IsIconic(a.Hwnd);

    /// <summary>
    /// The Claude window the user is in: the one in front, or the one that was in front before
    /// Tandem's own menu or the tray took the focus.
    /// </summary>
    public IntPtr CurrentWindow =>
        Active?.Hwnd ?? (_lastWindow != IntPtr.Zero && User32.IsWindow(_lastWindow) ? _lastWindow : IntPtr.Zero);

    public void Refresh() => Evaluate(User32.GetForegroundWindow());

    /// <summary>Re-reads the profile of the active window after the profile list changed.</summary>
    public void RefreshProfiles()
    {
        if (Active is { } active && _instances.FromProcess(active.Instance.ProcessId) is { } instance)
        {
            Active = active with { Instance = instance };
            LastProfile = instance.Profile ?? LastProfile;
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _foregroundHook.Dispose();
        _minimizeHook.Dispose();
        HookProcess(0);
    }

    private void OnSystemEvent(uint eventType, IntPtr hwnd, int idObject, int idChild)
    {
        if (idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF)
        {
            return;
        }

        switch (eventType)
        {
            case User32.EVENT_SYSTEM_FOREGROUND:
                Evaluate(hwnd);
                break;
            case User32.EVENT_SYSTEM_MINIMIZESTART or User32.EVENT_SYSTEM_MINIMIZEEND when Active?.Hwnd == hwnd:
                ActiveChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnWindowEvent(uint eventType, IntPtr hwnd, int idObject, int idChild)
    {
        if (Active is not { } active || hwnd != active.Hwnd || idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF)
        {
            return;
        }

        switch (eventType)
        {
            case User32.EVENT_OBJECT_LOCATIONCHANGE:
                ActiveMoved?.Invoke(this, EventArgs.Empty);
                break;
            case User32.EVENT_OBJECT_DESTROY or User32.EVENT_OBJECT_HIDE or User32.EVENT_OBJECT_CLOAKED:
                // Let the window finish closing or hiding, then look again.
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () => Evaluate(User32.GetForegroundWindow()));
                break;
            case User32.EVENT_OBJECT_SHOW or User32.EVENT_OBJECT_UNCLOAKED:
                ActiveChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void Evaluate(IntPtr foreground)
    {
        var next = Resolve(foreground);
        if (next?.Hwnd == Active?.Hwnd && next?.Instance.Profile?.Id == Active?.Instance.Profile?.Id && next is not null)
        {
            return;
        }

        Active = next;
        if (next is not null)
        {
            LastProfile = next.Profile ?? LastProfile;
            _lastWindow = next.Hwnd;
            HookProcess((uint)next.Instance.ProcessId);
        }
        else
        {
            HookProcess(0);
        }

        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private ActiveClaudeWindow? Resolve(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
        {
            return null;
        }

        var root = User32.GetAncestor(foreground, User32.GA_ROOTOWNER);
        if (root == IntPtr.Zero)
        {
            root = foreground;
        }

        var instance = _instances.FromWindow(root);
        if (instance is null)
        {
            return null;
        }

        var hwnd = WindowTools.IsAppWindow(root) && WindowTools.IsLargeEnough(root)
            ? root
            : WindowTools.FindMainWindow(instance.ProcessId);
        return hwnd == IntPtr.Zero ? null : new ActiveClaudeWindow(hwnd, instance);
    }

    private void HookProcess(uint processId)
    {
        if (processId == _hookedProcessId)
        {
            return;
        }

        foreach (var hook in _windowHooks)
        {
            hook.Dispose();
        }

        _windowHooks.Clear();
        _hookedProcessId = processId;
        if (processId != 0)
        {
            // Narrow ranges, only for the one Claude process in front.
            _windowHooks.Add(new WinEventHook(User32.EVENT_OBJECT_DESTROY, User32.EVENT_OBJECT_HIDE, OnWindowEvent, processId));
            _windowHooks.Add(new WinEventHook(User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE, OnWindowEvent, processId));
            _windowHooks.Add(new WinEventHook(User32.EVENT_OBJECT_CLOAKED, User32.EVENT_OBJECT_UNCLOAKED, OnWindowEvent, processId));
        }
    }
}
