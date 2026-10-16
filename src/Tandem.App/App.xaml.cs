using System.Windows;
using System.Windows.Threading;
using Tandem.App.Services;
using Tandem.App.Views;
using Tandem.Core;
using Tandem.Core.Logging;

namespace Tandem.App;

public partial class App : Application
{
    private readonly AppPaths _paths;
    private readonly SingleInstance? _instance;
    private readonly bool _showManageOnStart;
    private readonly (string Dir, bool Dark)? _preview;

    internal App(AppPaths paths, SingleInstance? instance, bool showManageOnStart, (string Dir, bool Dark)? preview = null)
    {
        _preview = preview;
        _paths = paths;
        _instance = instance;
        _showManageOnStart = showManageOnStart;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
    }

    public static new App Current => (App)Application.Current;

    internal AppHost? Host { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_preview is { } preview)
        {
            PreviewRenderer.Run(this, preview.Dir, preview.Dark);
            Shutdown(0);
            return;
        }

        try
        {
            Host = new AppHost(this, _paths);
            Host.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Tandem could not start.", ex);
            MessageDialog.Show(null, "Tandem could not start", ex.Message);
            Shutdown(1);
            return;
        }

        _instance?.ListenForShow(() => Dispatcher.BeginInvoke(() => Host?.ShowManage()));
        if (_showManageOnStart)
        {
            Host.ShowManage();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Host?.Dispose();
        Log.Info("Tandem exited.");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        try
        {
            MessageDialog.Show(
                null,
                "Something went wrong",
                "Tandem hit an unexpected error and kept running. Details are in the log file.\n\n" + e.Exception.Message);
        }
        catch (Exception)
        {
            // Never let the error dialog itself bring the app down.
        }
    }
}
