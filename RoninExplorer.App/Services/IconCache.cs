using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoninExplorer.Core.Engine.Native;

namespace RoninExplorer.App.Services;

/// <summary>
/// Extracts and caches shell icons (via SHGetFileInfo) as frozen WPF
/// BitmapSources, keyed by file extension — the same icon is shared across
/// every file of a given type, matching how Explorer's icon cache behaves for
/// ordinary files. Folders share a single cached icon. Lives in the App
/// project (not Core) because ImageSource is a WPF/PresentationCore type and
/// Core is intentionally UI-agnostic.
/// </summary>
public static class IconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const string FolderKey = "\0folder";
    private const string LargeFolderKey = "\0folder-large";
    private const string LargePrefix = "\0large:";

    public static ImageSource GetFolderIcon()
        => Cache.GetOrAdd(FolderKey, static _ => ExtractIcon(
            "folder",
            NativeMethods.FILE_ATTRIBUTE_DIRECTORY,
            NativeMethods.SHGFI_SMALLICON));

    public static ImageSource GetFileIcon(string extension)
    {
        var key = string.IsNullOrEmpty(extension) ? string.Empty : extension;
        return Cache.GetOrAdd(key, static ext => ExtractIcon(
            string.IsNullOrEmpty(ext) ? "file" : "file" + ext,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            NativeMethods.SHGFI_SMALLICON));
    }

    /// <summary>32x32 folder icon, for view modes (e.g. Large Icons) that need more than a 16x16 list glyph.</summary>
    public static ImageSource GetLargeFolderIcon()
        => Cache.GetOrAdd(LargeFolderKey, static _ => ExtractIcon(
            "folder",
            NativeMethods.FILE_ATTRIBUTE_DIRECTORY,
            NativeMethods.SHGFI_LARGEICON));

    /// <summary>32x32 file-type icon, for view modes (e.g. Large Icons) that need more than a 16x16 list glyph.</summary>
    public static ImageSource GetLargeFileIcon(string extension)
    {
        var key = LargePrefix + (string.IsNullOrEmpty(extension) ? string.Empty : extension);
        return Cache.GetOrAdd(key, _ => ExtractIcon(
            string.IsNullOrEmpty(extension) ? "file" : "file" + extension,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            NativeMethods.SHGFI_LARGEICON));
    }

    private static ImageSource ExtractIcon(string fakePath, uint attributes, uint sizeFlag)
    {
        var info = new NativeMethods.SHFILEINFO();
        var flags = NativeMethods.SHGFI_ICON
                  | sizeFlag
                  | NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var result = NativeMethods.SHGetFileInfo(fakePath, attributes, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0 }, 4);

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }
}
