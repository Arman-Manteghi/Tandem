using System.Runtime.InteropServices;

namespace Tandem.App.Native;

/// <summary>A window's size, position and maximized state (standard window management only).</summary>
internal sealed record WindowSpot(WindowArrange.WINDOWPLACEMENT Placement, bool Maximized);

/// <summary>Puts one window exactly where another one is, so switching accounts feels like one window.</summary>
internal static class WindowArrange
{
    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_SHOWMINNOACTIVE = 7;

    public static WindowSpot? Capture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd) || User32.IsIconic(hwnd))
        {
            return null;
        }

        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        return GetWindowPlacement(hwnd, ref placement) ? new WindowSpot(placement, User32.IsZoomed(hwnd)) : null;
    }

    /// <summary>Applies a captured spot. Only resizable windows are moved (not small fixed dialogs).</summary>
    public static void Apply(IntPtr hwnd, WindowSpot spot)
    {
        if (!User32.IsWindow(hwnd) || !WindowTools.IsResizable(hwnd))
        {
            return;
        }

        var placement = spot.Placement;
        placement.length = Marshal.SizeOf<WINDOWPLACEMENT>();
        placement.flags = 0;
        placement.showCmd = spot.Maximized ? SW_SHOWMAXIMIZED : SW_SHOWNORMAL;
        SetWindowPlacement(hwnd, ref placement);
    }

    public static void MinimizeWithoutActivating(IntPtr hwnd)
    {
        if (User32.IsWindow(hwnd) && !User32.IsIconic(hwnd))
        {
            User32.ShowWindow(hwnd, SW_SHOWMINNOACTIVE);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public User32.POINT ptMinPosition;
        public User32.POINT ptMaxPosition;
        public User32.RECT rcNormalPosition;
    }
}
