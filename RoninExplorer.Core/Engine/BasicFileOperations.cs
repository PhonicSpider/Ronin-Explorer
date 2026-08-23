using System.Runtime.InteropServices;
using RoninExplorer.Core.Engine.Native;

namespace RoninExplorer.Core.Engine;

// ── Basic file operations ───────────────────────────────────────────────────
// Interactive new-folder/rename/copy/move via plain System.IO, notifying the
// shell namespace afterward via SHChangeNotify so other Explorer windows (and
// this app's own nav pane) don't go stale. This is the M2 bootstrap
// implementation — M7 replaces it with an IFileOperation-based
// FileOperationService, which additionally gets the native progress dialog,
// conflict-resolution UI, and shell undo-stack integration. Delete stays on
// RecycleBin.cs (SHFileOperation already notifies internally).
public static class BasicFileOperations
{
    /// <summary>
    /// Creates a new folder under <paramref name="parentPath"/>, deduplicating
    /// the name Explorer-style ("New folder", "New folder (2)", ...). Returns
    /// the full path of the created folder.
    /// </summary>
    public static string CreateFolder(string parentPath, string desiredName = "New folder")
    {
        var path = ResolveConflictName(parentPath, desiredName);
        Directory.CreateDirectory(path);
        NotifyShellUpdate(parentPath);
        return path;
    }

    /// <summary>
    /// Creates a new empty file under <paramref name="parentPath"/> with the
    /// same Explorer-style name dedup as <see cref="CreateFolder"/> — the
    /// engine side of the toolbar's "New" menu (Explorer's own "New &gt;
    /// Text Document" etc.). Returns the full path of the created file.
    /// </summary>
    public static string CreateFile(string parentPath, string desiredName)
    {
        var path = ResolveConflictName(parentPath, desiredName);
        File.WriteAllBytes(path, []);
        NotifyShellUpdate(parentPath);
        return path;
    }

    /// <summary>
    /// Creates a new file under <paramref name="parentPath"/> containing
    /// <paramref name="content"/>, with the same Explorer-style name dedup —
    /// used for "New" menu entries whose ShellNew registration carries a
    /// literal Data value (e.g. some Office document types).
    /// </summary>
    public static string CreateFileWithContent(string parentPath, string desiredName, byte[] content)
    {
        var path = ResolveConflictName(parentPath, desiredName);
        File.WriteAllBytes(path, content);
        NotifyShellUpdate(parentPath);
        return path;
    }

    /// <summary>
    /// Creates a new file under <paramref name="parentPath"/> by copying a
    /// ShellNew template file (e.g. from %SystemRoot%\ShellNew), with the
    /// same Explorer-style name dedup.
    /// </summary>
    public static string CreateFileFromTemplate(string parentPath, string desiredName, string templateSourcePath)
    {
        var path = ResolveConflictName(parentPath, desiredName);
        File.Copy(templateSourcePath, path);
        NotifyShellUpdate(parentPath);
        return path;
    }

    /// <summary>Renames a file or folder in place. Fails if the target name is already taken.</summary>
    public static bool Rename(string path, string newName, out string error)
    {
        error = string.Empty;
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            error = "Cannot rename a drive root.";
            return false;
        }

        var newPath = Path.Combine(parent, newName);
        bool isDir = Directory.Exists(path);

        if (!string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase)
            && (Directory.Exists(newPath) || File.Exists(newPath)))
        {
            error = "An item with that name already exists.";
            return false;
        }

        try
        {
            if (isDir) Directory.Move(path, newPath);
            else File.Move(path, newPath, overwrite: false);

            NotifyShellUpdate(parent);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Copies each source file/folder into <paramref name="destFolder"/>, deduplicating name conflicts.</summary>
    public static void CopyToFolder(IEnumerable<string> sourcePaths, string destFolder)
    {
        foreach (var src in sourcePaths)
        {
            var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
            var dest = ResolveConflictName(destFolder, name);
            if (Directory.Exists(src))
                CopyDirectoryRecursive(src, dest);
            else
                File.Copy(src, dest);
        }
        NotifyShellUpdate(destFolder);
    }

    /// <summary>Moves each source file/folder into <paramref name="destFolder"/>, deduplicating name conflicts.</summary>
    public static void MoveToFolder(IEnumerable<string> sourcePaths, string destFolder)
    {
        foreach (var src in sourcePaths)
        {
            var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
            var dest = ResolveConflictName(destFolder, name);
            if (Directory.Exists(src))
                Directory.Move(src, dest);
            else
                File.Move(src, dest);
        }
        NotifyShellUpdate(destFolder);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var entry in new DirectoryInfo(sourceDir).EnumerateFileSystemInfos())
        {
            if (FileSystemHelpers.IsReparsePoint(entry)) continue; // don't follow junctions/symlinks
            var target = Path.Combine(destDir, entry.Name);
            if (entry is DirectoryInfo) CopyDirectoryRecursive(entry.FullName, target);
            else File.Copy(entry.FullName, target);
        }
    }

    /// <summary>Computes the deduplicated path a new item at <paramref name="name"/> would get, without creating anything — used by NewItemTemplateService's Command-based creation, which needs the path before the external process/service writes the file itself.</summary>
    internal static string ResolveConflictName(string folder, string name)
    {
        var candidate = Path.Combine(folder, name);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
            return candidate;

        var ext = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        int suffix = 2;
        string next;
        do
        {
            next = Path.Combine(folder, $"{baseName} ({suffix}){ext}");
            suffix++;
        } while (Directory.Exists(next) || File.Exists(next));

        return next;
    }

    private static void NotifyShellUpdate(string folderPath)
    {
        var ptr = Marshal.StringToHGlobalUni(folderPath);
        try
        {
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_UPDATEDIR, NativeMethods.SHCNF_PATHW, ptr, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
