using System.Windows;
using System.Windows.Controls;

namespace Tandem.App.Controls;

/// <summary>
/// Tandem's own title bar for windows that use WindowChrome: a drag area plus caption buttons.
/// Also keeps the content inside the screen when the window is maximized.
/// </summary>
public partial class TitleBar : UserControl
{
    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public bool ShowMinimize
    {
        get => MinimizeButton.Visibility == Visibility.Visible;
        set => MinimizeButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool ShowMaximize
    {
        get => MaximizeButton.Visibility == Visibility.Visible;
        set => MaximizeButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            return;
        }

        _window = Window.GetWindow(this);
        if (_window is null)
        {
            return;
        }

        _window.StateChanged += (_, _) => UpdateState();
        UpdateState();
    }

    private void UpdateState()
    {
        if (_window is null)
        {
            return;
        }

        var maximized = _window.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";

        // A maximized WindowChrome window extends past the screen by its resize border.
        if (_window.Content is FrameworkElement root)
        {
            var border = SystemParameters.WindowResizeBorderThickness;
            root.Margin = maximized
                ? new Thickness(border.Left + 2, border.Top + 2, border.Right + 2, border.Bottom + 2)
                : new Thickness(0);
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => _window?.Close();
}
