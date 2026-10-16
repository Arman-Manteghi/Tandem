namespace Tandem.Core.Settings;

public enum OverlayCorner
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Where the switch button goes on a Claude window. The button is attached to one corner;
/// its offsets are measured from that corner's two edges, in device-independent pixels, so it
/// keeps its place when the window is resized or moved to a monitor with another scale.
/// </summary>
public static class OverlayGeometry
{
    /// <summary>
    /// Default offsets per corner. Top-right sits left of the window's caption buttons
    /// (like a browser's profile button); bottom corners keep a small inset.
    /// </summary>
    public static (double X, double Y) DefaultOffset(OverlayCorner corner) => corner switch
    {
        OverlayCorner.TopRight => (152, 6),
        OverlayCorner.TopLeft => (128, 6),
        _ => (16, 16),
    };

    /// <summary>Top-left position of the button, kept fully inside the window frame.</summary>
    public static (int X, int Y) Place(PixelRect frame, int width, int height, OverlayCorner corner, double offsetX, double offsetY, double scale)
    {
        var ox = (int)Math.Round(Math.Max(0, offsetX) * scale);
        var oy = (int)Math.Round(Math.Max(0, offsetY) * scale);
        var x = IsLeft(corner) ? frame.Left + ox : frame.Right - ox - width;
        var y = IsTop(corner) ? frame.Top + oy : frame.Bottom - oy - height;
        x = Math.Clamp(x, frame.Left, Math.Max(frame.Left, frame.Right - width));
        y = Math.Clamp(y, frame.Top, Math.Max(frame.Top, frame.Bottom - height));
        return (x, y);
    }

    /// <summary>After a drag: the nearest corner and the offsets from it.</summary>
    public static (OverlayCorner Corner, double OffsetX, double OffsetY) Anchor(PixelRect frame, PixelRect button, double scale)
    {
        if (scale <= 0)
        {
            scale = 1;
        }

        var left = button.Left - frame.Left;
        var right = frame.Right - button.Right;
        var top = button.Top - frame.Top;
        var bottom = frame.Bottom - button.Bottom;
        var useLeft = left <= right;
        var useTop = top <= bottom;
        var corner = (useTop, useLeft) switch
        {
            (true, true) => OverlayCorner.TopLeft,
            (true, false) => OverlayCorner.TopRight,
            (false, true) => OverlayCorner.BottomLeft,
            _ => OverlayCorner.BottomRight,
        };
        var x = Math.Round(Math.Max(0, useLeft ? left : right) / scale);
        var y = Math.Round(Math.Max(0, useTop ? top : bottom) / scale);
        return (corner, x, y);
    }

    private static bool IsLeft(OverlayCorner corner) => corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft;

    private static bool IsTop(OverlayCorner corner) => corner is OverlayCorner.TopLeft or OverlayCorner.TopRight;
}
