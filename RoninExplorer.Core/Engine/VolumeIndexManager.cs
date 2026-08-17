using System.IO;
using RoninExplorer.Core.Models;
using RoninExplorer.Core.Services;

namespace RoninExplorer.Core.Engine;

// ── Volume index manager ────────────────────────────────────────────────────
// Owns one MftIndexEngine result per fixed NTFS drive and exposes a
// thread-safe search surface over all of them combined. This is the piece
// that makes search "instant" — querying an already-built in-memory index
// instead of walking the disk on every keystroke.
//
// Deliberately scoped for this milestone: builds happen on demand (call
// BuildAllAsync once at startup, or RefreshAsync to rebuild), not via
// incremental USN-journal deltas. Keeping the index fresh as files change
// without a full rebuild is real additional complexity (FSCTL_QUERY_USN_JOURNAL
// + FSCTL_READ_USN_JOURNAL) — flagged as a known follow-up rather than
// built here, so this ships correct rather than half-working.
public sealed class VolumeIndexManager
{
    private readonly Dictionary<string, List<VolumeIndexEntry>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public bool IsBuilding { get; private set; }
    public bool HasAnyIndex { get { lock (_lock) return _indexes.Count > 0; } }
    public IReadOnlyList<string> IndexedDrives { get { lock (_lock) return [.. _indexes.Keys]; } }

    /// <summary>
    /// Builds an index for every fixed NTFS drive. No-op (index stays empty)
    /// when not elevated — callers should fall back to LiveWalkSearchEngine.
    /// </summary>
    public async Task BuildAllAsync(CancellationToken ct = default)
    {
        if (!ElevationService.IsElevated) return;

        IsBuilding = true;
        try
        {
            var ntfsDrives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady
                    && string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.RootDirectory.FullName)
                .ToList();

            var engine = new MftIndexEngine();
            foreach (var drive in ntfsDrives)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var entries = await engine.BuildAsync(drive, ct);
                    lock (_lock) { _indexes[drive] = entries; }
                }
                catch
                {
                    // A single drive failing to index (locked volume, permissions,
                    // mid-operation journal churn) shouldn't block the others.
                }
            }
        }
        finally
        {
            IsBuilding = false;
        }
    }

    /// <summary>Substring or wildcard search across every indexed drive, capped at <paramref name="maxResults"/>.</summary>
    public List<VolumeIndexEntry> Search(string query, int maxResults = 500)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        bool isWildcard = query.Contains('*') || query.Contains('?');

        List<List<VolumeIndexEntry>> snapshot;
        lock (_lock) { snapshot = [.. _indexes.Values]; }

        var results = new List<VolumeIndexEntry>();
        foreach (var index in snapshot)
        {
            foreach (var entry in index)
            {
                if (!FileSystemHelpers.MatchesQuery(entry.Name, query, isWildcard)) continue;
                results.Add(entry);
                if (results.Count >= maxResults) return results;
            }
        }
        return results;
    }
}
