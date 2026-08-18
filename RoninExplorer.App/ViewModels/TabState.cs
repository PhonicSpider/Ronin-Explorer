using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoninExplorer.App.ViewModels;

/// <summary>
/// One tab's navigation state: current folder plus its own back/forward
/// history. Deliberately does NOT duplicate the nav pane, file list items,
/// sort/view-mode, or Details/Tools panel state — in real Explorer the nav
/// pane is shared across all tabs in a window (it just highlights whichever
/// tab is active), and everything else is inherently tied to "the folder
/// currently showing," which MainViewModel keeps live-synced to whichever
/// tab is active rather than each tab carrying its own copy.
/// </summary>
public sealed partial class TabState : ObservableObject
{
    [ObservableProperty]
    private string _currentPath;

    [ObservableProperty]
    private string _title;

    internal readonly Stack<string> Back = [];
    internal readonly Stack<string> Forward = [];

    public TabState(string currentPath)
    {
        _currentPath = currentPath;
        _title = DeriveTitle(currentPath);
    }

    partial void OnCurrentPathChanged(string value) => Title = DeriveTitle(value);

    private static string DeriveTitle(string path)
    {
        if (string.IsNullOrEmpty(path)) return "This PC";
        var name = Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name; // drive roots ("C:\") have no file name — show the root itself
    }
}
