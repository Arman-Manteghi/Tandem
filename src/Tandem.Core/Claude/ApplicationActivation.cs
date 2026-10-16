using System.Runtime.InteropServices;

namespace Tandem.Core.Claude;

/// <summary>
/// Starts a packaged app by its AUMID with arguments (documented shell API). Used only when the
/// user has turned off the app execution alias.
/// </summary>
internal static class ApplicationActivation
{
    public static int Activate(string aumid, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            var hr = manager.ActivateApplication(aumid, arguments, 0, out var processId);
            Marshal.ThrowExceptionForHR(hr);
            return (int)processId;
        }
        finally
        {
            Marshal.FinalReleaseComObject(manager);
        }
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            int options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager;
}
