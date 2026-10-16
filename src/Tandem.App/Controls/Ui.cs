using System.Windows;
using System.Windows.Media;

namespace Tandem.App.Controls;

/// <summary>Attached properties shared by the control templates in Themes/Controls.xaml.</summary>
public static class Ui
{
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
        "PressedBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(6)));

    public static Brush GetHoverBackground(DependencyObject d) => (Brush)d.GetValue(HoverBackgroundProperty);

    public static void SetHoverBackground(DependencyObject d, Brush value) => d.SetValue(HoverBackgroundProperty, value);

    public static Brush GetPressedBackground(DependencyObject d) => (Brush)d.GetValue(PressedBackgroundProperty);

    public static void SetPressedBackground(DependencyObject d, Brush value) => d.SetValue(PressedBackgroundProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);
}
