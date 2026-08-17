using System.IO;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

// ── Cleanup scanner ─────────────────────────────────────────────────────────
// Finds files matching an age-based cleanup rule: under a folder, matching a
// name pattern, and older than a cutoff. Adapted near-verbatim from
// Ronin_Disk_Manager's Engine/CleanupScanner.cs — SearchResult retargeted to
// SearchHit (drops Disk-Manager-only free-space-target fields).
public static class CleanupScanner
{
    /// <summary>
    /// True when a file should be cleaned: its name matches <paramref name="pattern"/>
    /// (wildcards or substring) and it was last modified before <paramref name="cutoff"/>.
    /// </summary>
    public static bool ShouldClean(string name, string pattern, DateTime lastWrite, DateTime cutoff)
    {
        bool nameOk = string.IsNullOrWhiteSpace(pattern)
            || FileSystemHelpers.MatchesQuery(name, pattern);
        return nameOk && lastWrite < cutoff;
    }

    /// <summary>
    /// Enumerates files under <paramref name="folder"/> that satisfy the rule.
    /// Runs off the UI thread, honors cancellation, and skips reparse points.
    /// </summary>
    public static Task<List<SearchHit>> FindAsync(
        string folder,
        string pattern,
        int olderThanDays,
        bool recurse,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            var results = new List<SearchHit>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return results;

            var cutoff = DateTime.Now.AddDays(-Math.Max(0, olderThanDays));
            var dirs = new Stack<string>();
            dirs.Push(folder);

            while (dirs.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                DirectoryInfo di;
                try { di = new DirectoryInfo(dirs.Pop()); } catch { continue; }

                progress?.Report($"Scanning {di.FullName}");

                IEnumerable<FileSystemInfo> entries;
                try { entries = di.EnumerateFileSystemInfos(); }
                catch { continue; }

                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry is DirectoryInfo sub)
                    {
                        if (recurse && !FileSystemHelpers.IsReparsePoint(sub))
                            dirs.Push(sub.FullName);
                    }
                    else if (entry is FileInfo fi)
                    {
                        try
                        {
                            if (ShouldClean(fi.Name, pattern, fi.LastWriteTime, cutoff))
                                results.Add(new SearchHit
                                {
                                    Name = fi.Name,
                                    FullPath = fi.FullName,
                                    IsDirectory = false,
                                    SizeBytes = fi.Length,
                                    DateModified = fi.LastWriteTime,
                                });
                        }
                        catch { /* inaccessible file — skip */ }
                    }
                }
            }

            return results.OrderByDescending(r => r.SizeBytes).ToList();
        }, ct);
}
