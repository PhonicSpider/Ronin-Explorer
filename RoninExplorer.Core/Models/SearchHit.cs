namespace RoninExplorer.Core.Models;

/// <summary>A single hit from LiveWalkSearchEngine or SearchIndexEngine — adapted from Disk Manager's SearchResult, dropping the Disk-Manager-only free-space-target fields.</summary>
public sealed class SearchHit
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime DateModified { get; init; }
}
