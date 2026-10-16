using System.Windows;
using Microsoft.Win32;
using Tandem.App.Native;
using Tandem.Core.Settings;

namespace Tandem.App.Services;

/// <summary>Applies the light or dark palette (following Windows, or fixed) to every Tandem window.</summary>
internal sealed class ThemeService : IDisposable
{
    private static readonly Uri LightUri = new("pack://application:,,,/Tandem;component/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Tandem;component/Themes/Dark.xaml");
    private readonly Application _app;
    private readonly List<WeakReference<Window>> _windows = [];
    private ThemeMode _mode;
    private bool _applied;

    public ThemeService(Application app, ThemeMode mode)
    {
        _app = app;
        _mode = mode;
        IsDark = Resolve(mode);
        ApplyResources();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? Changed;

    public bool IsDark { get; private set; }

    /// <summary>Dark title bar for a window, now and whenever the theme changes.</summary>
    public void Attach(Window window)
    {
        _windows.Add(new WeakReference<Window>(window));
        if (new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            WindowTools.ApplyTitleBarTheme(window, IsDark);
        }
        else
        {
            window.SourceInitialized += (_, _) => WindowTools.ApplyTitleBarTheme(window, IsDark);
        }
    }

    public void SetMode(ThemeMode mode)
    {
        _mode = mode;
        Update();
    }

    /// <summary>Uses a fixed theme (for rendering previews).</summary>
    public void Force(bool dark)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _mode = dark ? ThemeMode.Dark : ThemeMode.Light;
        IsDark = dark;
        ApplyResources();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
        {
            _app.Dispatcher.BeginInvoke(Update);
        }
    }

    private void Update()
    {
        var dark = Resolve(_mode);
        if (dark == IsDark && _applied)
        {
            return;
        }

        IsDark = dark;
        ApplyResources();
        foreach (var weak in _windows.ToArray())
        {
            if (weak.TryGetTarget(out var window))
            {
                WindowTools.ApplyTitleBarTheme(window, IsDark);
            }
            else
            {
                _windows.Remove(weak);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyResources()
    {
        var dictionaries = _app.Resources.MergedDictionaries;
        var theme = new ResourceDictionary { Source = IsDark ? DarkUri : LightUri };
        if (dictionaries.Count > 0 && dictionaries[0].Source is { } source
            && (source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                || source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)))
        {
            dictionaries[0] = theme;
        }
        else
        {
            dictionaries.Insert(0, theme);
        }

        _applied = true;
    }

    private static bool Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => WindowsUsesDarkApps(),
    };

    private static bool WindowsUsesDarkApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
