namespace Tandem.Core.Settings;

/// <summary>Where account windows go when Tandem arranges them.</summary>
public static class WindowLayout
{
    /// <summary>
    /// Splits a work area for <paramref name="count"/> windows: up to three side by side, then a
    /// grid. A shorter last row is stretched so the area is always filled. Neighbouring cells are
    /// separated by <paramref name="gap"/> pixels; the outer edges touch the work area.
    /// </summary>
    public static IReadOnlyList<PixelRect> Grid(PixelRect area, int count, int gap = 0)
    {
        if (count <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return [];
        }

        var columns = count <= 3 ? count : (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        var half = Math.Max(0, gap) / 2;
        var result = new List<PixelRect>(count);

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var inRow = row < rows - 1 ? columns : count - (columns * (rows - 1));
            var col = i - (row * columns);

            var left = area.Left + (int)((long)area.Width * col / inRow);
            var right = area.Left + (int)((long)area.Width * (col + 1) / inRow);
            var top = area.Top + (int)((long)area.Height * row / rows);
            var bottom = area.Top + (int)((long)area.Height * (row + 1) / rows);

            result.Add(new PixelRect(
                col == 0 ? left : left + half,
                row == 0 ? top : top + half,
                col == inRow - 1 ? right : right - half,
                row == rows - 1 ? bottom : bottom - half));
        }

        return result;
    }
}
