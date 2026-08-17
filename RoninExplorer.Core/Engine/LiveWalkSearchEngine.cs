using System.IO;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

// ── Live-walk search engine ─────────────────────────────────────────────────
// Fallback for when VolumeIndexManager's MFT index isn't available (not
// elevated, or a non-NTFS drive): searches by recursively walking every fixed
// drive. Adapted from Ronin_Disk_Manager's Engine/SearchEngine.cs — this is
// NOT the fast path; it's the same speed as stock Explorer's own filename
// search, present so search still works (just not instantly) without admin.
public static class LiveWalkSearchEngine
{
    public static async Task<List<SearchHit>> SearchAsync(
        string query,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        bool isWildcard = query.Contains('*') || query.Contains('?');
        var normQuery = query.Trim();

        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
        if (drives.Count == 0) return [];

        var driveTasks = drives.Select(drive => Task.Run(() => SearchDrive(drive, normQuery, isWildcard, progress, ct), ct));
        var buckets = await Task.WhenAll(driveTasks);
        return [.. buckets.SelectMany(b => b)];
    }

    private static List<SearchHit> SearchDrive(DriveInfo drive, string query, bool isWildcard, IProgress<string>? progress, CancellationToken ct)
    {
        var hits = new List<SearchHit>();
        var stack = new Stack<DirectoryInfo>();

        try { stack.Push(new DirectoryInfo(drive.RootDirectory.FullName)); }
        catch { return hits; }

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            if (dir.Parent?.Parent == null)
                progress?.Report($"Searching {dir.FullName}");

            IEnumerable<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly); }
            catch { continue; }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry is DirectoryInfo subDir && !FileSystemHelpers.IsReparsePoint(subDir))
                {
                    try { stack.Push(subDir); } catch { /* inaccessible — skip */ }
                }

                if (!FileSystemHelpers.MatchesQuery(entry.Name, query, isWildcard)) continue;

                bool isDir = entry is DirectoryInfo;
                long size = 0;
                DateTime modified = DateTime.MinValue;
                try
                {
                    modified = entry.LastWriteTime;
                    if (!isDir && entry is FileInfo fi) size = fi.Length;
                }
                catch { /* metadata unavailable — leave defaults */ }

                hits.Add(new SearchHit
                {
                    Name = entry.Name,
                    FullPath = entry.FullName,
                    IsDirectory = isDir,
                    SizeBytes = size,
                    DateModified = modified,
                });
            }
        }

        return hits;
    }
}
