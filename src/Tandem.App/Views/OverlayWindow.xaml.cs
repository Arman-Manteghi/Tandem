using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tandem.App.Native;

namespace Tandem.App.Views;

/// <summary>
/// The small switch button shown on the active Claude window. It never takes focus
/// (WS_EX_NOACTIVATE). A click opens the switcher; dragging moves it.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>Transparent margin around the pill, room for its shadow (DIPs).</summary>
    public const double ShadowMargin = 8;

    /// <summary>Movement (physical pixels) after which a press becomes a drag.</summary>
    private const int DragThreshold = 4;

    private bool _pressed;
    private bool _dragging;
    private User32.POINT _pressCursor;
    private User32.RECT _pressWindow;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTools.AddExtendedStyle(this, User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW);
    }

    public event EventHandler? Clicked;

    /// <summary>Raised after a drag with the final window rectangle in physical pixels.</summary>
    internal event EventHandler<User32.RECT>? Moved;

    public IntPtr Hwnd => WindowTools.Handle(this);

    public void SetProfile(string name, string color)
    {
        NameText.Text = name;
        InitialText.Text = ProfileRow.InitialOf(name);
        Avatar.Background = (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        User32.GetCursorPos(out _pressCursor);
        User32.GetWindowRect(Hwnd, out _pressWindow);
        _pressed = CaptureMouse();
        _dragging = false;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pressed)
        {
            return;
        }

        User32.GetCursorPos(out var cursor);
        var dx = cursor.X - _pressCursor.X;
        var dy = cursor.Y - _pressCursor.Y;
        if (!_dragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
        {
            _dragging = true;
            Pill.Cursor = Cursors.SizeAll;
        }

        if (_dragging)
        {
            WindowTools.MoveTopmost(Hwnd, _pressWindow.Left + dx, _pressWindow.Top + dy);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasDragging = _dragging;
        var wasPressed = _pressed;
        EndPress();
        if (wasDragging)
        {
            User32.GetWindowRect(Hwnd, out var rect);
            Moved?.Invoke(this, rect);
        }
        else if (wasPressed)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging)
        {
            User32.GetWindowRect(Hwnd, out var rect);
            Moved?.Invoke(this, rect);
        }

        _pressed = false;
        _dragging = false;
        Pill.Cursor = Cursors.Hand;
    }

    private void EndPress()
    {
        _pressed = false;
        _dragging = false;
        Pill.Cursor = Cursors.Hand;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }
}
