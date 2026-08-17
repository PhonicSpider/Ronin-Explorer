using System.IO;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

// ── Folder listing service ──────────────────────────────────────────────────
// Lists a single folder's contents directly via Directory.EnumerateFileSystemInfos,
// bypassing the Win32 shell namespace layer (the same technique Disk Manager's
// FallbackScanEngine already uses). This alone is fast enough for everyday
// browsing — Explorer's perceived slowness comes from shell namespace/thumbnail
// overhead, not from directory enumeration itself. The MFT-backed volume index
// (a later milestone) exists to accelerate whole-volume search, not this.
public static class FolderListingService
{
    /// <summary>
    /// Lists the immediate children of <paramref name="folderPath"/>. Folders
    /// sort before files, then alphabetically by name — matching Explorer's
    /// default "Name" sort. Entries that throw while reading metadata are
    /// skipped rather than aborting the whole listing.
    /// </summary>
    public static Task<List<FileSystemEntry>> ListFolderAsync(
        string folderPath,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            var results = new List<FileSystemEntry>();

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folderPath).EnumerateFileSystemInfos();
            }
            catch (UnauthorizedAccessException) { return results; }
            catch (IOException) { return results; }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    bool isDir = entry is DirectoryInfo;
                    results.Add(new FileSystemEntry
                    {
                        Name = entry.Name,
                        FullPath = entry.FullName,
                        IsDirectory = isDir,
                        SizeBytes = !isDir && entry is FileInfo fi ? fi.Length : 0,
                        DateModified = entry.LastWriteTime,
                        DateCreated = entry.CreationTime,
                        Extension = isDir ? string.Empty : entry.Extension,
                        Attributes = entry.Attributes,
                    });
                }
                catch { /* inaccessible/vanished entry — skip */ }
            }

            return [.. results
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        }, ct);

    /// <summary>
    /// Lists fixed, ready drives for the "This PC" nav-pane node. Excludes
    /// optical/network/removable drives that aren't ready to avoid the classic
    /// "no disk in drive" dialog on enumeration.
    /// </summary>
    public static List<FileSystemEntry> ListFixedDrives()
        => [.. DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new FileSystemEntry
            {
                Name = FormatDriveLabel(d),
                FullPath = d.RootDirectory.FullName,
                IsDirectory = true,
                SizeBytes = d.TotalSize - d.AvailableFreeSpace,
                DateModified = DateTime.MinValue,
                DateCreated = DateTime.MinValue,
            })];

    private static string FormatDriveLabel(DriveInfo drive)
    {
        var root = drive.RootDirectory.FullName.TrimEnd('\\');
        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
        return $"{label} ({root})";
    }
}
