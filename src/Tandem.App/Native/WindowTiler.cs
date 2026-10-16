using System.Runtime.InteropServices;
using Tandem.Core.Settings;

namespace Tandem.App.Native;

/// <summary>Arranges account windows side by side (ordinary window management, like snapping).</summary>
internal static class WindowTiler
{
    private const int Gap = 8;
    private const uint SWP_FLAGS = User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER;

    /// <param name="acrossScreens">Spread the windows over every monitor instead of one.</param>
    /// <param name="screenOf">A window on the monitor to use when not spreading.</param>
    public static int Arrange(IReadOnlyList<IntPtr> windows, bool acrossScreens, IntPtr screenOf)
    {
        var usable = windows.Where(h => User32.IsWindow(h) && WindowTools.IsResizable(h)).ToList();
        if (usable.Count == 0)
        {
            return 0;
        }

        var areas = acrossScreens ? MonitorWorkAreas() : [WorkAreaOf(screenOf != IntPtr.Zero ? screenOf : usable[0])];
        if (areas.Count == 0)
        {
            return 0;
        }

        // Round-robin over monitors, then a grid on each monitor.
        for (var m = 0; m < areas.Count; m++)
        {
            var group = usable.Where((_, i) => i % areas.Count == m).ToList();
            var scale = DpiScaleOf(areas[m]);
            var cells = WindowLayout.Grid(areas[m], group.Count, (int)Math.Round(Gap * scale));
            for (var i = 0; i < group.Count; i++)
            {
                Place(group[i], cells[i]);
            }
        }

        return usable.Count;
    }

    private static void Place(IntPtr hwnd, PixelRect target)
    {
        if (User32.IsIconic(hwnd) || User32.IsZoomed(hwnd))
        {
            User32.ShowWindow(hwnd, User32.SW_RESTORE);
        }

        // Twice: a window that lands on a monitor with another scale resizes itself once.
        for (var pass = 0; pass < 2; pass++)
        {
            User32.GetWindowRect(hwnd, out var window);
            var frame = WindowTools.GetFrameBounds(hwnd);

            // The window rectangle includes invisible resize borders; the frame is what you see.
            var left = frame.Left - window.Left;
            var top = frame.Top - window.Top;
            var right = window.Right - frame.Right;
            var bottom = window.Bottom - frame.Bottom;
            User32.SetWindowPos(hwnd, IntPtr.Zero,
                target.Left - left, target.Top - top,
                target.Width + left + right, target.Height + top + bottom, SWP_FLAGS);
        }
    }

    private static PixelRect WorkAreaOf(IntPtr hwnd)
    {
        var work = WindowTools.GetWorkArea(User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST));
        return new PixelRect(work.Left, work.Top, work.Right, work.Bottom);
    }

    private static List<PixelRect> MonitorWorkAreas()
    {
        var result = new List<PixelRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var work = WindowTools.GetWorkArea(monitor);
            result.Add(new PixelRect(work.Left, work.Top, work.Right, work.Bottom));
            return true;
        }, IntPtr.Zero);
        return result.OrderBy(r => r.Left).ThenBy(r => r.Top).ToList();
    }

    private static double DpiScaleOf(PixelRect area)
    {
        var monitor = User32.MonitorFromPoint(new User32.POINT { X = area.Left + 1, Y = area.Top + 1 }, User32.MONITOR_DEFAULTTONEAREST);
        return User32.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
}
