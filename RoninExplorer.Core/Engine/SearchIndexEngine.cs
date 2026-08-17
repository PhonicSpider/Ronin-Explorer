using System.IO;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

// ── Search index engine ─────────────────────────────────────────────────────
// The search entry point the UI calls: instant results from VolumeIndexManager
// when an MFT index is available, LiveWalkSearchEngine otherwise. USN records
// carry no size/date, so index hits get those fetched lazily per displayed
// result (cheap — capped result sets, not the whole index) rather than eagerly
// for every indexed file.
public sealed class SearchIndexEngine(VolumeIndexManager indexManager)
{
    public async Task<List<SearchHit>> SearchAsync(
        string query,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (indexManager.HasAnyIndex)
            return [.. indexManager.Search(query).Select(ToSearchHit)];

        return await LiveWalkSearchEngine.SearchAsync(query, progress, ct);
    }

    private static SearchHit ToSearchHit(VolumeIndexEntry entry)
    {
        long size = 0;
        var modified = DateTime.MinValue;

        try
        {
            if (entry.IsDirectory)
            {
                modified = Directory.GetLastWriteTime(entry.FullPath);
            }
            else
            {
                var fi = new FileInfo(entry.FullPath);
                size = fi.Length;
                modified = fi.LastWriteTime;
            }
        }
        catch { /* vanished or became inaccessible since the index was built — leave defaults */ }

        return new SearchHit
        {
            Name = entry.Name,
            FullPath = entry.FullPath,
            IsDirectory = entry.IsDirectory,
            SizeBytes = size,
            DateModified = modified,
        };
    }
}
