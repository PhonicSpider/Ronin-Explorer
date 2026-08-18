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
// Kept live via a periodic incremental USN-journal delta read (ReadDeltaAsync)
// rather than a full FSCTL_ENUM_USN_DATA rebuild — a background timer applies
// only what changed since the last cursor. A new top-level item created
// directly at a drive root (whose parent is the unindexed volume-root record)
// won't be picked up by a delta until the next full rebuild; this is a
// deliberate, narrow gap rather than added complexity for an edge case.
public sealed class VolumeIndexManager
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private sealed class DriveIndex
    {
        public required Dictionary<ulong, VolumeIndexEntry> Entries;
        public required ulong JournalId;
        public required long NextUsn;
    }

    private readonly Dictionary<string, DriveIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly MftIndexEngine _engine = new();
    private Timer? _refreshTimer;

    public bool IsBuilding { get; private set; }
    public bool HasAnyIndex { get { lock (_lock) return _indexes.Count > 0; } }
    public IReadOnlyList<string> IndexedDrives { get { lock (_lock) return [.. _indexes.Keys]; } }

    /// <summary>
    /// Builds an index for every fixed NTFS drive. No-op (index stays empty)
    /// when not elevated — callers should fall back to LiveWalkSearchEngine.
    /// Starts the periodic incremental-refresh timer once at least one drive
    /// is indexed.
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

            foreach (var drive in ntfsDrives)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = await _engine.BuildAsync(drive, ct);
                    lock (_lock)
                    {
                        _indexes[drive] = new DriveIndex
                        {
                            Entries = result.EntriesByFrn,
                            JournalId = result.JournalId,
                            NextUsn = result.StartUsn,
                        };
                    }
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

        if (HasAnyIndex)
            _refreshTimer ??= new Timer(_ => _ = RefreshAllAsync(), null, RefreshInterval, RefreshInterval);
    }

    /// <summary>Reads and applies USN-journal deltas for every indexed drive since its last cursor. Called periodically by the background timer; safe to call directly (e.g. from a manual "refresh now" action).</summary>
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        List<(string Drive, ulong JournalId, long NextUsn)> snapshot;
        lock (_lock) { snapshot = [.. _indexes.Select(kv => (kv.Key, kv.Value.JournalId, kv.Value.NextUsn))]; }

        foreach (var (drive, journalId, nextUsn) in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var delta = await _engine.ReadDeltaAsync(drive, journalId, nextUsn, ct);
                if (delta.Changes.Count == 0 && delta.NextUsn == nextUsn) continue;

                lock (_lock)
                {
                    if (_indexes.TryGetValue(drive, out var idx))
                        ApplyDelta(idx, delta);
                }
            }
            catch
            {
                // Journal reset/deleted (rare — e.g. a defrag or journal-size
                // change) invalidates the cursor. Skip this cycle; the next
                // full BuildAllAsync (relaunch, for now) recovers.
            }
        }
    }

    private static void ApplyDelta(DriveIndex idx, DeltaResult delta)
    {
        foreach (var change in delta.Changes)
        {
            if (change.IsDelete)
            {
                idx.Entries.Remove(change.Frn);
                continue;
            }

            // Resolve the full path via the parent's already-known path. If
            // the parent isn't indexed (e.g. it's the un-indexed volume-root
            // record, or a change arrived before its parent's), skip — the
            // next full rebuild self-heals.
            if (!idx.Entries.TryGetValue(change.ParentFrn, out var parent)) continue;

            idx.Entries[change.Frn] = new VolumeIndexEntry
            {
                Name = change.Name,
                FullPath = Path.Combine(parent.FullPath, change.Name),
                IsDirectory = change.IsDirectory,
                Frn = change.Frn,
                ParentFrn = change.ParentFrn,
            };
        }
        idx.NextUsn = delta.NextUsn;
    }

    /// <summary>Substring or wildcard search across every indexed drive, capped at <paramref name="maxResults"/>.</summary>
    public List<VolumeIndexEntry> Search(string query, int maxResults = 500)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        bool isWildcard = query.Contains('*') || query.Contains('?');

        List<Dictionary<ulong, VolumeIndexEntry>> snapshot;
        lock (_lock) { snapshot = [.. _indexes.Values.Select(idx => idx.Entries)]; }

        var results = new List<VolumeIndexEntry>();
        foreach (var index in snapshot)
        {
            foreach (var entry in index.Values)
            {
                if (!FileSystemHelpers.MatchesQuery(entry.Name, query, isWildcard)) continue;
                results.Add(entry);
                if (results.Count >= maxResults) return results;
            }
        }
        return results;
    }
}
