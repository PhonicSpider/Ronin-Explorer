using System.Runtime.InteropServices;

namespace RoninExplorer.Core.Engine.Native;

// ── IFileOperation COM interop ──────────────────────────────────────────────
// Hand-declared COM interfaces for the modern Windows shell file-operation
// API (shobjidl.h), which SHFileOperation/RecycleBin.cs's classic API has
// been deprecated in favor of since Vista. Gives interactive copy/move/
// rename/delete the native "flying files" progress dialog, automatic
// conflict-resolution UI (skip/replace/rename), and — for Delete — a proper
// entry on the shell's undo stack, none of which the M2 bootstrap
// (BasicFileOperations/RecycleBin) provides.
//
// COM vtable dispatch is positional: every method between IUnknown and the
// ones actually used here MUST be declared, in the exact order shobjidl.h
// defines them, even though most are never called — omitting or reordering
// one silently shifts every later call onto the wrong vtable slot. IShellItem
// is deliberately left with NO methods declared: every place this code uses
// IShellItem it's only ever passed as an opaque pointer into IFileOperation's
// own methods, never invoked directly, so its vtable layout is irrelevant
// here (its interface identity — the GUID — is what SHCreateItemFromParsingName
// and IFileOperation's marshaling actually need).
internal static class ComInterop
{
    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
    }

    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        void Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems(IntPtr punkItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink? pfopsItem);
        void RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);
        void MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IFileOperationProgressSink? pfopsItem);
        void CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);
        void DeleteItems(IntPtr punkItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IFileOperationProgressSink? pfopsItem);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetAnyOperationsAborted();
    }

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    internal class FileOperationClass
    {
    }

    // FOF_ALLOWUNDO: Recycle Bin for deletes, and a shell undo-stack entry
    // for every operation (matches how real Explorer performs these).
    internal const uint FOF_ALLOWUNDO = 0x0040;
    internal const uint FOFX_NOMINIMIZEBOX = 0x00200000;

    private static readonly Guid IID_IShellItem = typeof(IShellItem).GUID;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    internal static IShellItem CreateShellItem(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItem, out var item);
        return item;
    }
}
