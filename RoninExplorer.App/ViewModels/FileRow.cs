using System.Windows.Media;
using RoninExplorer.App.Services;
using RoninExplorer.Core.Engine;
using RoninExplorer.Core.Models;

namespace RoninExplorer.App.ViewModels;

/// <summary>Presentation wrapper around a FileSystemEntry — adds the resolved icon and display strings.</summary>
public sealed class FileRow(FileSystemEntry entry)
{
    public FileSystemEntry Entry { get; } = entry;

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public string FileType => Entry.FileType;
    public DateTime DateModified => Entry.DateModified;

    public string SizeDisplay => Entry.IsDirectory ? string.Empty : FileSystemHelpers.FormatBytes(Entry.SizeBytes);

    public ImageSource Icon => Entry.IsDirectory
        ? IconCache.GetFolderIcon()
        : IconCache.GetFileIcon(Entry.Extension);
}
