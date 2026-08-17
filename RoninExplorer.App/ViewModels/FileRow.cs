using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RoninExplorer.App.Services;
using RoninExplorer.Core.Engine;
using RoninExplorer.Core.Models;

namespace RoninExplorer.App.ViewModels;

/// <summary>Presentation wrapper around a FileSystemEntry — adds the resolved icon, display strings, and inline-rename state.</summary>
public partial class FileRow(FileSystemEntry entry) : ObservableObject
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

    public ImageSource LargeIcon => Entry.IsDirectory
        ? IconCache.GetLargeFolderIcon()
        : IconCache.GetLargeFileIcon(Entry.Extension);

    /// <summary>True while this row is showing an inline rename TextBox instead of its name TextBlock.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>Bound to the inline rename TextBox; seeded with the current name when rename starts.</summary>
    [ObservableProperty]
    private string _editName = entry.Name;
}
