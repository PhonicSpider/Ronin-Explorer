namespace RoninExplorer.Core.Models;

/// <summary>A single file/folder entry in an MftIndexEngine whole-volume index. Deliberately minimal — no size/date, since USN records don't carry those and the index favors instant name search over eager metadata.</summary>
public sealed class VolumeIndexEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
}
