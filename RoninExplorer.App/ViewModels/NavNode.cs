using System.Collections.ObjectModel;
using System.Windows.Media;
using RoninExplorer.App.Services;

namespace RoninExplorer.App.ViewModels;

/// <summary>A row in the Win11-style nav pane: a pinned/This-PC/drive entry, optionally with children.</summary>
public sealed class NavNode(string name, string? path)
{
    public string Name { get; } = name;

    /// <summary>Null for section headers (e.g. "This PC") that aren't themselves navigable.</summary>
    public string? Path { get; } = path;

    public ImageSource? Icon => Path is null ? null : IconCache.GetFolderIcon();

    public ObservableCollection<NavNode> Children { get; } = [];

    public bool IsExpanded { get; set; } = true;
}
