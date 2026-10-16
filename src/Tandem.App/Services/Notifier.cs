namespace Tandem.App.Services;

/// <summary>Small, non-blocking messages shown by the tray icon.</summary>
internal sealed class Notifier
{
    private TrayIconService? _tray;

    public void Attach(TrayIconService tray) => _tray = tray;

    public void Info(string title, string text) => _tray?.Show(title, text, System.Windows.Forms.ToolTipIcon.Info);

    public void Warning(string title, string text) => _tray?.Show(title, text, System.Windows.Forms.ToolTipIcon.Warning);

    public void Error(string title, string text) => _tray?.Show(title, text, System.Windows.Forms.ToolTipIcon.Error);
}
