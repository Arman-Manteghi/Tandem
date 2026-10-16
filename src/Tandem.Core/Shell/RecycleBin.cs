using System.Runtime.InteropServices;

namespace Tandem.Core.Shell;

public enum RecycleResult
{
    Moved,
    NotFound,
    Cancelled,
}

/// <summary>Moves files and folders to the Recycle Bin. Tandem never deletes profile data permanently.</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <param name="owner">Window that owns any dialog Windows shows (for example "too big to recycle").</param>
    public static RecycleResult Move(string path, IntPtr owner = default)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full) && !File.Exists(full))
        {
            return RecycleResult.NotFound;
        }

        var operation = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = full + "\0\0",
            // FOF_WANTNUKEWARNING makes Windows ask before anything would be deleted permanently
            // instead of being recycled (for example when the item is too large for the bin).
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOCONFIRMMKDIR | FOF_WANTNUKEWARNING,
        };

        var rc = SHFileOperationW(ref operation);
        if (operation.fAnyOperationsAborted)
        {
            return RecycleResult.Cancelled;
        }

        if (rc != 0)
        {
            throw new IOException($"Windows could not move the item to the Recycle Bin (error 0x{rc:X}). Is it still in use?");
        }

        return Directory.Exists(full) || File.Exists(full) ? RecycleResult.Cancelled : RecycleResult.Moved;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT fileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
}
