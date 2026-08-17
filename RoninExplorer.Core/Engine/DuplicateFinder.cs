using System.IO;
using System.Security.Cryptography;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

// ── Duplicate finder ────────────────────────────────────────────────────────
// Finds byte-for-byte duplicate files among a flat file list. Files are first
// grouped by size (a cheap, exact pre-filter — different sizes can never be
// equal), then only same-size candidates are hashed with SHA-256 to confirm
// equality. Adapted from Ronin_Disk_Manager's Engine/DuplicateFinder.cs —
// retargeted from DiskNode (a scanned tree) to a flat FileSystemEntry list
// (from FolderListingService.ListFilesRecursiveAsync), since the Tools panel
// scopes a scan to the current folder rather than a whole pre-scanned volume.
public static class DuplicateFinder
{
    /// <summary>A set of files that are byte-for-byte identical.</summary>
    public sealed record DuplicateGroup
    {
        public long SizeBytes { get; init; }
        public string Hash { get; init; } = string.Empty;
        public List<FileSystemEntry> Files { get; init; } = [];
        public int Count => Files.Count;
        /// <summary>Space reclaimable if all but one copy were removed.</summary>
        public long WastedBytes => SizeBytes * (Count - 1);
    }

    public static List<List<FileSystemEntry>> GroupBySizeCandidates(IEnumerable<FileSystemEntry> files)
        => files
            .Where(f => !f.IsDirectory && f.SizeBytes > 0)
            .GroupBy(f => f.SizeBytes)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

    public static Task<List<DuplicateGroup>> FindDuplicatesAsync(
        IEnumerable<FileSystemEntry> files,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            var candidates = GroupBySizeCandidates(files);
            int total = candidates.Sum(g => g.Count);
            int hashed = 0;
            var groups = new List<DuplicateGroup>();

            foreach (var sizeGroup in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var byHash = new Dictionary<string, List<FileSystemEntry>>();
                foreach (var file in sizeGroup)
                {
                    ct.ThrowIfCancellationRequested();
                    var hash = TryHash(file.FullPath);
                    hashed++;
                    if (hash == null) continue;

                    if (!byHash.TryGetValue(hash, out var list))
                        byHash[hash] = list = [];
                    list.Add(file);

                    if (hashed % 200 == 0)
                        progress?.Report($"Hashing {hashed:N0} / {total:N0} candidate files");
                }

                foreach (var kv in byHash.Where(kv => kv.Value.Count > 1))
                    groups.Add(new DuplicateGroup
                    {
                        SizeBytes = kv.Value[0].SizeBytes,
                        Hash = kv.Key,
                        Files = kv.Value
                    });
            }

            return groups.OrderByDescending(g => g.WastedBytes).ToList();
        }, ct);

    private static string? TryHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null; // locked / inaccessible files are skipped
        }
    }
}
