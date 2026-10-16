using Microsoft.Win32.SafeHandles;
using Tandem.Core.Interop;

namespace Tandem.Core.Processes;

/// <summary>
/// Read-only process metadata (image path, start time, package, command line, parent).
/// Uses PROCESS_QUERY_LIMITED_INFORMATION only; never reads process memory.
/// </summary>
internal static unsafe class ProcessQuery
{
    public static SafeProcessHandle? Open(int processId)
    {
        var handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }

    public static string? GetImagePath(SafeProcessHandle process)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return Kernel32.QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    public static DateTime? GetStartTimeUtc(SafeProcessHandle process) =>
        Kernel32.GetProcessTimes(process, out var creation, out _, out _, out _)
            ? DateTime.FromFileTimeUtc(creation)
            : null;

    public static string? GetPackageFamilyName(SafeProcessHandle process)
    {
        var buffer = stackalloc char[256];
        uint length = 256;
        return Kernel32.GetPackageFamilyName(process, ref length, buffer) == Kernel32.ERROR_SUCCESS
            ? new string(buffer, 0, (int)Math.Max(0, length - 1))
            : null;
    }

    public static string? GetPackageFullName(SafeProcessHandle process)
    {
        var buffer = stackalloc char[256];
        uint length = 256;
        return Kernel32.GetPackageFullName(process, ref length, buffer) == Kernel32.ERROR_SUCCESS
            ? new string(buffer, 0, (int)Math.Max(0, length - 1))
            : null;
    }

    /// <summary>
    /// The process command line. Only used to find <c>--type</c> and <c>--user-data-dir</c>;
    /// it is never logged or stored, because launcher processes can carry claude:// links.
    /// </summary>
    public static string? GetCommandLine(SafeProcessHandle process)
    {
        var status = Kernel32.NtQueryInformationProcess(process, Kernel32.ProcessCommandLineInformation, null, 0, out var needed);
        if (status != Kernel32.STATUS_INFO_LENGTH_MISMATCH || needed <= 0 || needed > (1 << 20))
        {
            return null;
        }

        var buffer = new byte[needed];
        fixed (byte* p = buffer)
        {
            status = Kernel32.NtQueryInformationProcess(process, Kernel32.ProcessCommandLineInformation, p, needed, out _);
            if (status != 0)
            {
                return null;
            }

            var text = (Kernel32.UNICODE_STRING*)p;
            return text->Buffer == null ? string.Empty : new string(text->Buffer, 0, text->Length / sizeof(char));
        }
    }

    public static int? GetParentProcessId(SafeProcessHandle process)
    {
        Kernel32.PROCESS_BASIC_INFORMATION info;
        var status = Kernel32.NtQueryInformationProcess(
            process, Kernel32.ProcessBasicInformation, &info, sizeof(Kernel32.PROCESS_BASIC_INFORMATION), out _);
        return status == 0 ? (int)info.InheritedFromUniqueProcessId : null;
    }
}
