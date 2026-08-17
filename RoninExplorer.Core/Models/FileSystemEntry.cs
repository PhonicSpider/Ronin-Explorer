namespace RoninExplorer.Core.Models;

/// <summary>
/// A single file or folder row for browsing a directory — the plain-listing
/// counterpart to Disk Manager's DiskNode (which carries tree/aggregation
/// fields that plain browsing doesn't need).
/// </summary>
public sealed class FileSystemEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime DateModified { get; init; }
    public DateTime DateCreated { get; init; }
    public string Extension { get; init; } = string.Empty;
    public FileAttributes Attributes { get; init; }

    /// <summary>
    /// File type description consistent with Explorer: "File Folder",
    /// "PNG File", "File", etc.
    /// </summary>
    public string FileType => IsDirectory
        ? "File Folder"
        : string.IsNullOrEmpty(Extension)
            ? "File"
            : Extension.TrimStart('.').ToUpperInvariant() + " File";
}
