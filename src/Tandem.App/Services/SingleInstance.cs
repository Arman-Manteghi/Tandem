namespace Tandem.App.Services;

/// <summary>One tray app per user session. A second start asks the first one to show its window.</summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Tandem.App";
    private const string ShowEventName = @"Local\Tandem.App.Show";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _wait;
    private bool _disposed;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    public static bool IsRunning => Mutex.TryOpenExisting(MutexName, out var existing) && Dispose(existing);

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    public void ListenForShow(Action onShow) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wait?.Unregister(null);
        _showEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread any more; disposing is enough.
        }

        _mutex.Dispose();
    }

    private static bool Dispose(IDisposable disposable)
    {
        disposable.Dispose();
        return true;
    }
}
