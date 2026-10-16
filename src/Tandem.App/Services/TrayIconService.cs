using System.IO;
using Tandem.Core.Profiles;
using Tandem.Core.Shell;
using Forms = System.Windows.Forms;

namespace Tandem.App.Services;

/// <summary>Tray icon tinted with the current account's color. Clicking it opens the switcher.</summary>
internal sealed class TrayIconService : IDisposable
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48];
    private readonly Forms.NotifyIcon _icon;
    private System.Drawing.Icon? _current;
    private string? _currentColor;

    public TrayIconService()
    {
        _icon = new Forms.NotifyIcon { Text = "Tandem", Visible = false };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        };
        _icon.DoubleClick += (_, _) => DoubleClicked?.Invoke(this, EventArgs.Empty);
        SetColor(IconFactory.AppBackground);
    }

    public event EventHandler? Clicked;

    public event EventHandler? DoubleClicked;

    public void Show() => _icon.Visible = true;

    public void Update(Profile? current)
    {
        SetColor(current?.Color ?? IconFactory.AppBackground);
        var text = current is null ? "Tandem" : $"Tandem – {current.DisplayName}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Show(string title, string text, Forms.ToolTipIcon kind)
    {
        // Windows keeps at most 63 characters of a balloon title and 255 of its text.
        if (_icon.Visible && text.Length > 0)
        {
            _icon.ShowBalloonTip(5000, Clip(title, 63), Clip(text, 255), kind);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }

    private void SetColor(string color)
    {
        if (string.Equals(color, _currentColor, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var bytes = IconFactory.CreateIco(color, IconFactory.Cream, IconSizes, rim: color == IconFactory.AppBackground);
        using var stream = new MemoryStream(bytes);
        var icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        _icon.Icon = icon;
        _current?.Dispose();
        _current = icon;
        _currentColor = color;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
