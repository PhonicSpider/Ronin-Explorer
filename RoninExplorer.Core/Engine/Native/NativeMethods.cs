using System.Runtime.InteropServices;

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
}
