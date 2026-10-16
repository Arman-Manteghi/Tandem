using System.Windows.Controls;
using System.Windows.Input;
using Tandem.App.Services;

namespace Tandem.App.Controls;

/// <summary>A text box that records a key combination (needs Ctrl, Alt or Win plus a key).</summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        // Implicit styles do not apply to subclasses: use the app's TextBox style explicitly.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
    }

    public event EventHandler<string>? GestureChanged;

    public string Gesture
    {
        get => Text;
        set => Text = value;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab)
        {
            e.Handled = false;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            modifiers |= ModifierKeys.Windows;
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            return;
        }

        var gesture = HotkeyService.Format(modifiers, key);
        if (gesture != Text)
        {
            Text = gesture;
            GestureChanged?.Invoke(this, gesture);
        }
    }
}
