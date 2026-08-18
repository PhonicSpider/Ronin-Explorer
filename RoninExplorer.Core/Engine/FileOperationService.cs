using System.Runtime.InteropServices;
using RoninExplorer.Core.Engine.Native;
using static RoninExplorer.Core.Engine.Native.ComInterop;

namespace RoninExplorer.Core.Engine;

// ── Native file operation service (M7) ──────────────────────────────────────
// Interactive copy/move/rename/delete via IFileOperation — the native
// progress dialog, automatic conflict-resolution UI (skip/replace/rename),
// and a shell undo-stack entry for every call, none of which the M2
// bootstrap (BasicFileOperations/RecycleBin) provides. Must be called on the
// UI (STA) thread, synchronously — IFileOperation is a classic COM object
// that expects STA and runs its own nested message loop internally while its
// progress UI is up; calling it from a background Task would fight that.
public static class FileOperationService
{
    public sealed record OperationResult(bool Success, bool WasAborted, string? Error)
    {
        public static readonly OperationResult Ok = new(true, false, null);
    }

    public static OperationResult CopyItems(IEnumerable<string> sourcePaths, string destFolder, IntPtr ownerHwnd = default)
        => Execute(fo =>
        {
            var dest = CreateShellItem(destFolder);
            foreach (var src in sourcePaths)
                fo.CopyItem(CreateShellItem(src), dest, null, null);
        }, ownerHwnd);

    public static OperationResult MoveItems(IEnumerable<string> sourcePaths, string destFolder, IntPtr ownerHwnd = default)
        => Execute(fo =>
        {
            var dest = CreateShellItem(destFolder);
            foreach (var src in sourcePaths)
                fo.MoveItem(CreateShellItem(src), dest, null, null);
        }, ownerHwnd);

    public static OperationResult RenameItem(string path, string newName, IntPtr ownerHwnd = default)
        => Execute(fo => fo.RenameItem(CreateShellItem(path), newName, null), ownerHwnd);

    public static OperationResult DeleteItems(IEnumerable<string> paths, IntPtr ownerHwnd = default)
        => Execute(fo =>
        {
            foreach (var path in paths)
                fo.DeleteItem(CreateShellItem(path), null);
        }, ownerHwnd);

    private static OperationResult Execute(Action<IFileOperation> configure, IntPtr ownerHwnd)
    {
        IFileOperation? fo = null;
        try
        {
            fo = (IFileOperation)new FileOperationClass();
            fo.SetOperationFlags(FOF_ALLOWUNDO);
            if (ownerHwnd != IntPtr.Zero) fo.SetOwnerWindow(ownerHwnd);

            configure(fo);

            fo.PerformOperations();
            var aborted = fo.GetAnyOperationsAborted();
            return aborted ? new OperationResult(false, true, "Operation was cancelled.") : OperationResult.Ok;
        }
        catch (COMException ex)
        {
            return new OperationResult(false, false, ex.Message);
        }
        finally
        {
            if (fo is not null) Marshal.ReleaseComObject(fo);
        }
    }
}
