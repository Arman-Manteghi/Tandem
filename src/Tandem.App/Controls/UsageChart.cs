using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tandem.Core.Usage;

namespace Tandem.App.Controls;

/// <summary>
/// The last seven days of an account's readings: the weekly limit as a filled step line and the
/// 5-hour limit as a thin line. Hovering shows the reading under the pointer.
/// </summary>
public sealed class UsageChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = Register<IReadOnlyList<UsageSample>?>(nameof(Samples), null);
    public static readonly DependencyProperty NowProperty = Register(nameof(Now), DateTimeOffset.MinValue);
    public static readonly DependencyProperty WeeklyBrushProperty = Register<Brush>(nameof(WeeklyBrush), Brushes.IndianRed);
    public static readonly DependencyProperty WeeklyFillProperty = Register<Brush>(nameof(WeeklyFill), Brushes.MistyRose);
    public static readonly DependencyProperty FiveHourBrushProperty = Register<Brush>(nameof(FiveHourBrush), Brushes.Gray);
    public static readonly DependencyProperty GridBrushProperty = Register<Brush>(nameof(GridBrush), Brushes.LightGray);
    public static readonly DependencyProperty LabelBrushProperty = Register<Brush>(nameof(LabelBrush), Brushes.Gray);
    public static readonly DependencyProperty TipBackgroundProperty = Register<Brush>(nameof(TipBackground), Brushes.White);
    public static readonly DependencyProperty TipForegroundProperty = Register<Brush>(nameof(TipForeground), Brushes.Black);

    private static readonly TimeSpan Span = TimeSpan.FromDays(7);

    /// <summary>A gap longer than this breaks the 5-hour line: the app was closed.</summary>
    private static readonly TimeSpan LineGap = TimeSpan.FromMinutes(45);
    private const double LabelHeight = 18;
    private const double AxisWidth = 34;
    private int _hover = -1;

    public UsageChart()
    {
        MouseMove += (_, e) => SetHover(Nearest(e.GetPosition(this).X));
        MouseLeave += (_, _) => SetHover(-1);
    }

    public IReadOnlyList<UsageSample>? Samples
    {
        get => (IReadOnlyList<UsageSample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>The right edge of the chart. Unset means the current time.</summary>
    public DateTimeOffset Now
    {
        get => (DateTimeOffset)GetValue(NowProperty);
        set => SetValue(NowProperty, value);
    }

    public Brush WeeklyBrush
    {
        get => (Brush)GetValue(WeeklyBrushProperty);
        set => SetValue(WeeklyBrushProperty, value);
    }

    public Brush WeeklyFill
    {
        get => (Brush)GetValue(WeeklyFillProperty);
        set => SetValue(WeeklyFillProperty, value);
    }

    public Brush FiveHourBrush
    {
        get => (Brush)GetValue(FiveHourBrushProperty);
        set => SetValue(FiveHourBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush TipBackground
    {
        get => (Brush)GetValue(TipBackgroundProperty);
        set => SetValue(TipBackgroundProperty, value);
    }

    public Brush TipForeground
    {
        get => (Brush)GetValue(TipForegroundProperty);
        set => SetValue(TipForegroundProperty, value);
    }

    private DateTimeOffset End => Now == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : Now;

    private Rect Plot => new(0, 6, Math.Max(0, ActualWidth - AxisWidth), Math.Max(0, ActualHeight - LabelHeight - 6));

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot;
        if (plot.Width < 40 || plot.Height < 20)
        {
            return;
        }

        // Transparent background so the whole area reacts to the pointer.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var grid = new Pen(GridBrush, 1) { DashStyle = new DashStyle([2, 3], 0) };
        grid.Freeze();
        var end = End;
        var start = end - Span;

        // Horizontal guides at 0, 50 and 100 percent.
        foreach (var level in new[] { 0, 50, 100 })
        {
            var y = Math.Round(Y(plot, level)) + 0.5;
            dc.DrawLine(level == 0 ? new Pen(GridBrush, 1) : grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text($"{level}%", 10.5, LabelBrush, dpi);
            dc.DrawText(label, new Point(plot.Right + 6, y - (label.Height / 2)));
        }

        // Day labels at local midnights.
        var day = start.ToLocalTime().Date.AddDays(1);
        while (day < end.ToLocalTime())
        {
            var x = X(plot, new DateTimeOffset(day), start);
            dc.DrawLine(grid, new Point(Math.Round(x) + 0.5, plot.Top), new Point(Math.Round(x) + 0.5, plot.Bottom));
            var label = Text(day.ToString("ddd", CultureInfo.CurrentCulture), 10.5, LabelBrush, dpi);
            if (x + 4 + label.Width < plot.Right)
            {
                dc.DrawText(label, new Point(x + 4, plot.Bottom + 3));
            }

            day = day.AddDays(1);
        }

        var samples = Visible(start, end);
        if (samples.Count == 0)
        {
            var empty = Text("No readings in the last 7 days", 12, LabelBrush, dpi);
            dc.DrawText(empty, new Point(plot.Left + ((plot.Width - empty.Width) / 2), plot.Top + ((plot.Height - empty.Height) / 2)));
            return;
        }

        DrawWeekly(dc, plot, samples, start, end);
        DrawFiveHour(dc, plot, samples, start);
        if (_hover >= 0 && _hover < samples.Count)
        {
            DrawHover(dc, plot, samples[_hover], start, dpi);
        }
    }

    private static DependencyProperty Register<T>(string name, T fallback) =>
        DependencyProperty.Register(
            name, typeof(T), typeof(UsageChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    private static double Y(Rect plot, int percent) => plot.Bottom - (plot.Height * Math.Clamp(percent, 0, 100) / 100.0);

    private static double X(Rect plot, DateTimeOffset at, DateTimeOffset start) =>
        plot.Left + (plot.Width * Math.Clamp((at - start).Ticks / (double)Span.Ticks, 0, 1));

    private List<UsageSample> Visible(DateTimeOffset start, DateTimeOffset end) =>
        (Samples ?? []).Where(s => s.At >= start && s.At <= end).OrderBy(s => s.At).ToList();

    private void DrawWeekly(DrawingContext dc, Rect plot, List<UsageSample> samples, DateTimeOffset start, DateTimeOffset end)
    {
        var points = samples.Where(s => s.Weekly is not null).ToList();
        if (points.Count == 0)
        {
            return;
        }

        // Usage holds between readings, so a step line. It is carried on only a little past the
        // last reading: what happened after that is not known.
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            var first = new Point(X(plot, points[0].At, start), Y(plot, points[0].Weekly!.Value));
            l.BeginFigure(first, false, false);
            a.BeginFigure(new Point(first.X, plot.Bottom), true, true);
            a.LineTo(first, false, false);
            var last = first;
            for (var i = 1; i < points.Count; i++)
            {
                var x = X(plot, points[i].At, start);
                var y = Y(plot, points[i].Weekly!.Value);
                l.LineTo(new Point(x, last.Y), true, true);
                l.LineTo(new Point(x, y), true, true);
                a.LineTo(new Point(x, last.Y), false, false);
                a.LineTo(new Point(x, y), false, false);
                last = new Point(x, y);
            }

            var lastAt = points[^1].At + TimeSpan.FromMinutes(15);
            var right = X(plot, lastAt < end ? lastAt : end, start);
            l.LineTo(new Point(right, last.Y), true, true);
            a.LineTo(new Point(right, last.Y), false, false);
            a.LineTo(new Point(right, plot.Bottom), false, false);
        }

        line.Freeze();
        area.Freeze();
        dc.DrawGeometry(WeeklyFill, null, area);
        dc.DrawGeometry(null, new Pen(WeeklyBrush, 1.8) { LineJoin = PenLineJoin.Round }, line);
    }

    private void DrawFiveHour(DrawingContext dc, Rect plot, List<UsageSample> samples, DateTimeOffset start)
    {
        var points = samples.Where(s => s.FiveHour is not null).ToList();
        if (points.Count == 0)
        {
            return;
        }

        var pen = new Pen(FiveHourBrush, 1.3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var p = new Point(X(plot, points[i].At, start), Y(plot, points[i].FiveHour!.Value));
                var joined = i > 0 && points[i].At - points[i - 1].At <= LineGap;
                if (joined)
                {
                    g.LineTo(p, true, true);
                }
                else
                {
                    g.BeginFigure(p, false, false);

                    // A lone reading still shows, as a short tick.
                    var alone = i + 1 >= points.Count || points[i + 1].At - points[i].At > LineGap;
                    if (alone)
                    {
                        g.LineTo(new Point(p.X + 1.5, p.Y), true, true);
                    }
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawHover(DrawingContext dc, Rect plot, UsageSample sample, DateTimeOffset start, double dpi)
    {
        var x = X(plot, sample.At, start);
        dc.DrawLine(new Pen(LabelBrush, 1), new Point(Math.Round(x) + 0.5, plot.Top), new Point(Math.Round(x) + 0.5, plot.Bottom));
        if (sample.Weekly is { } weekly)
        {
            dc.DrawEllipse(WeeklyBrush, new Pen(TipBackground, 1.5), new Point(x, Y(plot, weekly)), 3.5, 3.5);
        }

        if (sample.FiveHour is { } fiveHour)
        {
            dc.DrawEllipse(FiveHourBrush, new Pen(TipBackground, 1.5), new Point(x, Y(plot, fiveHour)), 3.5, 3.5);
        }

        var local = sample.At.ToLocalTime();
        var when = local.ToString("ddd ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
        var values = $"5-hour {Show(sample.FiveHour)}  ·  Weekly {Show(sample.Weekly)}";
        var top = Text(when, 11, LabelBrush, dpi);
        var bottom = Text(values, 12, TipForeground, dpi);
        bottom.SetFontWeight(FontWeights.SemiBold);
        var width = Math.Max(top.Width, bottom.Width) + 20;
        var height = top.Height + bottom.Height + 12;
        var left = Math.Clamp(x - (width / 2), plot.Left, Math.Max(plot.Left, plot.Right - width));
        var box = new Rect(left, plot.Top, width, height);
        dc.DrawRoundedRectangle(TipBackground, new Pen(GridBrush, 1), box, 8, 8);
        dc.DrawText(top, new Point(box.Left + 10, box.Top + 6));
        dc.DrawText(bottom, new Point(box.Left + 10, box.Top + 6 + top.Height));

        static string Show(int? value) => value is { } v ? $"{v}%" : "–";
    }

    private int Nearest(double x)
    {
        var end = End;
        var start = end - Span;
        var samples = Visible(start, end);
        var plot = Plot;
        if (samples.Count == 0 || x < plot.Left - 4 || x > plot.Right + 4)
        {
            return -1;
        }

        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < samples.Count; i++)
        {
            var distance = Math.Abs(X(plot, samples[i].At, start) - x);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        // Only snap to a reading that is near the pointer.
        return bestDistance <= 24 ? best : -1;
    }

    private void SetHover(int index)
    {
        if (index != _hover)
        {
            _hover = index;
            InvalidateVisual();
        }
    }

    private FormattedText Text(string text, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)FindResource("Font.UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, dpi);
}
