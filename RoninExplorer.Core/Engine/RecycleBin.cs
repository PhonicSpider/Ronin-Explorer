using RoninExplorer.Core.Engine.Native;

namespace RoninExplorer.Core.Engine;

// ── Recycle Bin helper ──────────────────────────────────────────────────────
// Wraps SHFileOperation to send a file or folder to the Windows Recycle Bin
// (FO_DELETE + FOF_ALLOWUNDO) rather than deleting it permanently. This is the
// safer default for the Delete action so mistakes can be recovered.
//
// Copied near-verbatim from Ronin_Disk_Manager's Engine/RecycleBin.cs. Serves
// as the M2 bootstrap delete implementation — M7 upgrades interactive delete
// to IFileOperation, which additionally gets the native progress dialog and a
// proper shell undo-stack entry.
//
// Note: the classic shell API does not support extended-length ("\\?\") paths,
// so callers should fall back to a permanent delete for paths at or beyond the
// legacy MAX_PATH limit (see FileSystemHelpers.ExceedsLegacyMaxPath).
public static class RecycleBin
{
    /// <summary>
    /// Sends <paramref name="path"/> (file or directory) to the Recycle Bin.
    /// Returns true on success; on failure returns false and sets
    /// <paramref name="error"/> to the Win32 result code description.
    /// </summary>
    public static bool Send(string path, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Empty path.";
            return false;
        }

        // pFrom must be double-null terminated. Marshalling as LPWStr adds one
        // null, so append a second null explicitly.
        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            hwnd   = IntPtr.Zero,
            wFunc  = NativeMethods.FO_DELETE,
            pFrom  = path + '\0',
            pTo    = null,
            fFlags = (ushort)(NativeMethods.FOF_ALLOWUNDO
                            | NativeMethods.FOF_NOCONFIRMATION
                            | NativeMethods.FOF_NOERRORUI),
        };

        int result = NativeMethods.SHFileOperation(ref op);

        if (result != 0)
        {
            error = $"Recycle failed (SHFileOperation code 0x{result:X}).";
            return false;
        }

        if (op.fAnyOperationsAborted)
        {
            error = "Operation was cancelled.";
            return false;
        }

        return true;
    }
}
