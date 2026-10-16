namespace Tandem.App.Native;

/// <summary>
/// An out-of-context WinEvent hook: Windows posts events to Tandem's own UI thread.
/// Nothing is loaded into, or injected into, any other process.
/// </summary>
internal sealed class WinEventHook : IDisposable
{
    private readonly User32.WinEventProc _callback;
    private IntPtr _handle;

    public WinEventHook(uint eventMin, uint eventMax, Action<uint, IntPtr, int, int> handler, uint processId = 0)
    {
        _callback = (_, eventType, hwnd, idObject, idChild, _, _) => handler(eventType, hwnd, idObject, idChild);
        _handle = User32.SetWinEventHook(
            eventMin, eventMax, IntPtr.Zero, _callback, processId, 0,
            User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
