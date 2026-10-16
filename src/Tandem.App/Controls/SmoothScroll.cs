using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tandem.App.Controls;

/// <summary>
/// Eased mouse-wheel scrolling for a ScrollViewer: the wheel sets a target offset and the view
/// glides towards it, frame by frame, instead of jumping.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    private const double WheelStep = 0.9; // pixels per wheel delta unit (120 per notch)
    private const double Easing = 0.22;
    private static readonly Dictionary<ScrollViewer, double> Targets = [];

    /// <summary>The offset each gliding viewer was last moved to, to tell its own steps from other scrolling.</summary>
    private static readonly Dictionary<ScrollViewer, double> Steps = [];
    private static bool _rendering;

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            viewer.PreviewMouseWheel += OnWheel;
            viewer.ScrollChanged += OnScrollChanged;
        }
        else
        {
            viewer.PreviewMouseWheel -= OnWheel;
            viewer.ScrollChanged -= OnScrollChanged;
            Stop(viewer);
        }
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (viewer.ScrollableHeight <= 0)
        {
            return;
        }

        e.Handled = true;
        var from = Targets.TryGetValue(viewer, out var pending) ? pending : viewer.VerticalOffset;
        Targets[viewer] = Math.Clamp(from - (e.Delta * WheelStep), 0, viewer.ScrollableHeight);
        if (!_rendering)
        {
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>Scrolling by other means (scroll bar, keyboard) or a change of content cancels a glide.</summary>
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (!Targets.ContainsKey(viewer))
        {
            return;
        }

        var ownStep = Steps.TryGetValue(viewer, out var step) && Math.Abs(e.VerticalOffset - step) < 1;
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 || (e.VerticalChange != 0 && !ownStep))
        {
            Stop(viewer);
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        foreach (var (viewer, target) in Targets.ToArray())
        {
            var current = viewer.VerticalOffset;
            var distance = target - current;
            if (Math.Abs(distance) < 0.5 || !viewer.IsLoaded)
            {
                viewer.ScrollToVerticalOffset(target);
                Stop(viewer);
                continue;
            }

            var next = current + (distance * Easing);
            Steps[viewer] = next;
            viewer.ScrollToVerticalOffset(next);
        }

        if (Targets.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
        }
    }

    private static void Stop(ScrollViewer viewer)
    {
        Targets.Remove(viewer);
        Steps.Remove(viewer);
    }
}
