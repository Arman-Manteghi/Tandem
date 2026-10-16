using System.Windows.Input;
using System.Windows.Interop;
using Tandem.App.Native;
using Tandem.Core.Logging;

namespace Tandem.App.Services;

/// <summary>One system-wide hotkey (RegisterHotKey), received by a hidden message-only window.</summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x7A01;
    private static readonly IntPtr HwndMessage = new(-3);
    private readonly HwndSource _window;
    private bool _registered;

    public HotkeyService()
    {
        _window = new HwndSource(new HwndSourceParameters("TandemHotkey") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public string? Registered { get; private set; }

    /// <summary>Registers the gesture, replacing the previous one. Returns false if another app owns it.</summary>
    public bool TryRegister(string gesture)
    {
        Unregister();
        if (!TryParse(gesture, out var modifiers, out var key))
        {
            return false;
        }

        _registered = User32.RegisterHotKey(_window.Handle, HotkeyId, modifiers | User32.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key));
        Registered = _registered ? gesture : null;
        if (!_registered)
        {
            Log.Warn($"The hotkey {gesture} is already used by another app.");
        }

        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            User32.UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
            Registered = null;
        }
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    public static bool TryParse(string? gesture, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= User32.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= User32.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= User32.MOD_SHIFT;
                    break;
                case "win":
                    modifiers |= User32.MOD_WIN;
                    break;
                default:
                    if (key != Key.None || !Enum.TryParse(raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw, true, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        // A hotkey needs a modifier, so plain typing is never captured.
        return key != Key.None && (modifiers & (User32.MOD_CONTROL | User32.MOD_ALT | User32.MOD_WIN)) != 0;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        var name = key.ToString();
        parts.Add(name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]) ? name[1..] : name);
        return string.Join("+", parts);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }
}
