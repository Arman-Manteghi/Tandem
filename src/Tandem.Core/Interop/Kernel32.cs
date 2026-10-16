using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tandem.Core.Interop;

internal static unsafe class Kernel32
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    public const int ProcessBasicInformation = 0;
    public const int ProcessCommandLineInformation = 60;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, char* familyName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, char* fullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetPackagesByPackageFamily(string familyName, ref uint count, IntPtr* packageFullNames, ref uint bufferLength, char* buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, char* path);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, void* information, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
