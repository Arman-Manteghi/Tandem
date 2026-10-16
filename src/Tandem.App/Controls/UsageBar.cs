using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Tandem.App.Controls;

/// <summary>A rounded meter for a percentage; it glides to a new value.</summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageBar), new FrameworkPropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(UsageBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(UsageBar), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(UsageBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 to 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = height / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, width, height), radius, radius);
        var shown = Math.Clamp((double)GetValue(ShownProperty), 0, 100);
        if (shown > 0)
        {
            // Never thinner than a dot, so a small value is still visible.
            var fill = Math.Max(height, width * shown / 100);
            dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Min(fill, width), height), radius, radius);
        }
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (UsageBar)d;
        var target = Math.Clamp((double)e.NewValue, 0, 100);
        if (!bar.IsLoaded)
        {
            bar.BeginAnimation(ShownProperty, null);
            bar.SetValue(ShownProperty, target);
            return;
        }

        bar.BeginAnimation(ShownProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
