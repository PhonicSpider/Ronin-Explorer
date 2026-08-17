using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RoninExplorer.Core.Engine.Native;

// ── Native interop ──────────────────────────────────────────────────────────
// P/Invoke surface for shell icon extraction (SHGetFileInfo). Kept UI-agnostic
// (raw HICON handles) so this library has no WPF/PresentationCore dependency —
// converting a handle into a WPF ImageSource is the App layer's job
// (see RoninExplorer.App/Services/IconCache.cs).
public static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON              = 0x000000100;
    public const uint SHGFI_SMALLICON         = 0x000000001;
    public const uint SHGFI_LARGEICON         = 0x000000000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    public const uint FILE_ATTRIBUTE_NORMAL    = 0x00000080;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── Shell file operations (Recycle Bin) ─────────────────────────────────
    // SHFileOperation with FO_DELETE + FOF_ALLOWUNDO sends items to the Recycle
    // Bin instead of deleting them permanently. Works for both files and folders.
    // Declarations copied from Ronin_Disk_Manager's Engine/NativeMethods.cs.

    public const uint FO_DELETE             = 0x0003;
    public const ushort FOF_ALLOWUNDO       = 0x0040;  // send to Recycle Bin
    public const ushort FOF_NOCONFIRMATION  = 0x0010;  // we show our own prompt
    public const ushort FOF_NOERRORUI       = 0x0400;  // suppress OS error dialogs

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    // ── Shell change notification ───────────────────────────────────────────
    // Raw System.IO create/rename/move calls don't tell the shell namespace
    // anything happened, so other open Explorer windows (and this app's own
    // nav pane, once it watches folders) go stale. SHChangeNotify tells the
    // shell to refresh. Not needed by RecycleBin's SHFileOperation path (that
    // notifies internally) — only by BasicFileOperations' raw-IO paths.
    public const uint SHCNE_MKDIR         = 0x00000008;
    public const uint SHCNE_RMDIR         = 0x00000010;
    public const uint SHCNE_RENAMEFOLDER  = 0x00020000;
    public const uint SHCNE_RENAMEITEM    = 0x00000001;
    public const uint SHCNE_CREATE        = 0x00000002;
    public const uint SHCNE_UPDATEDIR     = 0x00001000;
    public const uint SHCNF_PATHW         = 0x0005;

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // ── USN journal (whole-volume MFT enumeration) ──────────────────────────
    // Declarations copied from Ronin_Disk_Manager's Engine/NativeMethods.cs —
    // the same WizTree technique (FSCTL_ENUM_USN_DATA on a raw volume handle)
    // powers RoninExplorer.Core.Engine.MftIndexEngine's whole-volume search index.

    public const uint GENERIC_READ               = 0x80000000;
    public const uint FILE_SHARE_READ            = 0x00000001;
    public const uint FILE_SHARE_WRITE           = 0x00000002;
    public const uint OPEN_EXISTING              = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    public const uint FSCTL_ENUM_USN_DATA = 0x900B3;
    public const int ERROR_HANDLE_EOF     = 38;

    [StructLayout(LayoutKind.Sequential)]
    public struct MFT_ENUM_DATA_V0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_RECORD_V2
    {
        public uint RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong FileReferenceNumber;
        public ulong ParentFileReferenceNumber;
        public long Usn;
        public long TimeStamp;
        public uint Reason;
        public uint SourceInfo;
        public uint SecurityId;
        public uint FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref MFT_ENUM_DATA_V0 lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
