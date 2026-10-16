using System.Text.Json;
using Tandem.Core.Logging;
using Tandem.Core.Routing;
using Tandem.Core.Storage;

namespace Tandem.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Which Claude windows show the account button.</summary>
public enum OverlayScope
{
    FrontWindow,
    EveryWindow,
}

/// <summary>What happens to the window of the account you switch away from.</summary>
public enum SwitchBehavior
{
    LeaveOpen,
    Minimize,
    Close,
}

public sealed class AppSettings
{
    public const int CurrentVersion = 2;
    public const string DefaultHotkey = "Ctrl+Alt+A";

    public int Version { get; set; } = CurrentVersion;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Show the floating switch button on Claude windows (off until the user turns it on).</summary>
    public bool OverlayEnabled { get; set; }

    /// <summary>Only the Claude window in front, or every open Claude window (to tell them apart).</summary>
    public OverlayScope OverlayScope { get; set; } = OverlayScope.FrontWindow;

    /// <summary>The window corner the button is attached to.</summary>
    public OverlayCorner OverlayCorner { get; set; } = OverlayCorner.TopRight;

    /// <summary>Distance from the corner's vertical edge, in device-independent pixels.</summary>
    public double OverlayOffsetX { get; set; } = OverlayGeometry.DefaultOffset(OverlayCorner.TopRight).X;

    /// <summary>Distance from the corner's horizontal edge, in device-independent pixels.</summary>
    public double OverlayOffsetY { get; set; } = OverlayGeometry.DefaultOffset(OverlayCorner.TopRight).Y;

    /// <summary>Put the account you switch to exactly where the current Claude window is.</summary>
    public bool OpenInSamePlace { get; set; } = true;

    public SwitchBehavior SwitchBehavior { get; set; } = SwitchBehavior.LeaveOpen;

    /// <summary>
    /// Before an added account starts, give it the original account's current app setup
    /// (MCP servers, extensions, preferences, SSH connections, Code presets).
    /// </summary>
    public bool SyncSetupFromOriginal { get; set; } = true;

    public bool HotkeyEnabled { get; set; } = true;

    public string Hotkey { get; set; } = DefaultHotkey;

    /// <summary>Where claude:// links go when no account is waiting for sign-in.</summary>
    public LinkFallback LinkFallback { get; set; } = LinkFallback.LastActive;

    public bool OnboardingDismissed { get; set; }

    /// <summary>Version 1 setting, read only to migrate it.</summary>
    public bool? CloseOtherOnSwitch { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Brings settings written by an older version up to date.</summary>
    internal void Migrate()
    {
        if (CloseOtherOnSwitch == true)
        {
            SwitchBehavior = SwitchBehavior.Close;
        }

        CloseOtherOnSwitch = null;

        if (Version < 2)
        {
            // Version 1 put the button at the bottom-left and showed it by default.
            OverlayEnabled = false;
            ResetOverlay(OverlayCorner.TopRight);
        }

        if (!double.IsFinite(OverlayOffsetX) || !double.IsFinite(OverlayOffsetY) || OverlayOffsetX < 0 || OverlayOffsetY < 0)
        {
            ResetOverlay(OverlayCorner);
        }

        Version = CurrentVersion;
    }

    public void ResetOverlay(OverlayCorner corner)
    {
        var (x, y) = OverlayGeometry.DefaultOffset(corner);
        OverlayCorner = corner;
        OverlayOffsetX = x;
        OverlayOffsetY = y;
    }
}

public sealed class SettingsStore
{
    private readonly string _file;
    private AppSettings _current;

    private SettingsStore(string file, AppSettings current)
    {
        _file = file;
        _current = current;
    }

    public event EventHandler? Changed;

    public AppSettings Current => _current.Clone();

    public static SettingsStore Load(AppPaths paths)
    {
        var settings = new AppSettings();
        if (File.Exists(paths.SettingsFile))
        {
            try
            {
                settings = JsonFile.Read<AppSettings>(paths.SettingsFile) ?? new AppSettings();
                settings.Migrate();
            }
            catch (JsonException ex)
            {
                var aside = JsonFile.Quarantine(paths.SettingsFile, DateTimeOffset.Now);
                Log.Warn($"settings.json was unreadable and was renamed to {Path.GetFileName(aside)}; using defaults.", ex);
                settings = new AppSettings();
            }
        }

        return new SettingsStore(paths.SettingsFile, settings);
    }

    public void Update(Action<AppSettings> change)
    {
        var next = _current.Clone();
        change(next);
        JsonFile.Write(_file, next);
        _current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
