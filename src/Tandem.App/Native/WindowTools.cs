using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Tandem.App.Native;

internal static class WindowTools
{
    private const string ChromiumWindowClass = "Chrome_WidgetWin_1";

    public static int GetProcessId(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    /// <summary>The visible frame in physical pixels (without the invisible resize borders).</summary>
    public static User32.RECT GetFrameBounds(IntPtr hwnd)
    {
        if (Dwm.DwmGetWindowAttribute(hwnd, Dwm.DWMWA_EXTENDED_FRAME_BOUNDS, out User32.RECT rect, 16) == 0)
        {
            return rect;
        }

        User32.GetWindowRect(hwnd, out rect);
        return rect;
    }

    public static bool IsCloaked(IntPtr hwnd) =>
        Dwm.DwmGetWindowAttribute(hwnd, Dwm.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static double GetScale(IntPtr hwnd)
    {
        var dpi = User32.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>
    /// A top-level, unowned, visible app window of the Chromium class: the kind of window
    /// Claude uses for its main window (as opposed to menus, tooltips or helper windows).
    /// </summary>
    public static bool IsAppWindow(IntPtr hwnd)
    {
        if (!User32.IsWindowVisible(hwnd) || User32.GetWindow(hwnd, User32.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var exStyle = (long)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0 || (exStyle & User32.WS_EX_NOACTIVATE) != 0)
        {
            return false;
        }

        var className = new StringBuilder(64);
        User32.GetClassName(hwnd, className, className.Capacity);
        return className.ToString() == ChromiumWindowClass;
    }

    /// <summary>A window the user can resize (not a small fixed dialog).</summary>
    public static bool IsResizable(IntPtr hwnd) =>
        ((long)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE) & User32.WS_THICKFRAME) != 0;

    /// <summary>Large enough to be a main window rather than a small popup.</summary>
    public static bool IsLargeEnough(IntPtr hwnd)
    {
        if (User32.IsIconic(hwnd))
        {
            return true;
        }

        var frame = GetFrameBounds(hwnd);
        var scale = GetScale(hwnd);
        return frame.Width >= 320 * scale && frame.Height >= 240 * scale;
    }

    /// <summary>The main window of a Claude process: the largest qualifying window, preferring non-minimized ones.</summary>
    public static IntPtr FindMainWindow(int processId)
    {
        var best = IntPtr.Zero;
        long bestScore = -1;
        User32.EnumWindows((hwnd, _) =>
        {
            if (GetProcessId(hwnd) != processId || !IsAppWindow(hwnd) || IsCloaked(hwnd) || !IsLargeEnough(hwnd))
            {
                return true;
            }

            var frame = GetFrameBounds(hwnd);
            long score = User32.IsIconic(hwnd) ? 1 : 1 + ((long)frame.Width * frame.Height);
            if (score > bestScore)
            {
                best = hwnd;
                bestScore = score;
            }

            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static IReadOnlyList<IntPtr> FindAppWindows(int processId)
    {
        var result = new List<IntPtr>();
        User32.EnumWindows((hwnd, _) =>
        {
            if (GetProcessId(hwnd) == processId && IsAppWindow(hwnd))
            {
                result.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Restores and focuses a window. Works when Tandem just received the user's input.</summary>
    public static bool Activate(IntPtr hwnd)
    {
        if (!User32.IsWindow(hwnd))
        {
            return false;
        }

        if (User32.IsIconic(hwnd))
        {
            User32.ShowWindow(hwnd, User32.SW_RESTORE);
        }

        User32.SetForegroundWindow(hwnd);
        return User32.GetForegroundWindow() == hwnd;
    }

    public static User32.RECT GetWorkArea(IntPtr monitor)
    {
        var info = new User32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<User32.MONITORINFO>() };
        User32.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    public static User32.RECT GetWorkAreaAt(int x, int y) =>
        GetWorkArea(User32.MonitorFromPoint(new User32.POINT { X = x, Y = y }, User32.MONITOR_DEFAULTTONEAREST));

    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    public static void AddExtendedStyle(Window window, long style)
    {
        var hwnd = Handle(window);
        var current = (long)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, new IntPtr(current | style));
    }

    public static void MoveTopmost(IntPtr hwnd, int x, int y, bool show = true) =>
        User32.SetWindowPos(hwnd, User32.HWND_TOPMOST, x, y, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER | (show ? User32.SWP_SHOWWINDOW : 0));

    public static void ApplyTitleBarTheme(Window window, bool dark)
    {
        var hwnd = Handle(window);
        var value = dark ? 1 : 0;
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
