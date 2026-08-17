using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninExplorer.Core.Engine;

namespace RoninExplorer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string ThisPcPath = ""; // sentinel: the "This PC" virtual root (lists drives)

    private readonly Stack<string> _back = [];
    private readonly Stack<string> _forward = [];

    [ObservableProperty]
    private string _currentPath = ThisPcPath;

    [ObservableProperty]
    private string _addressBarText = "This PC";

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<FileRow> Items { get; } = [];
    public ObservableCollection<NavNode> NavRoots { get; } = [];

    public MainViewModel()
    {
        BuildNavPane();
        _ = NavigateToAsync(ThisPcPath, recordHistory: false);
    }

    private void BuildNavPane()
    {
        var thisPc = new NavNode("This PC", ThisPcPath);
        foreach (var drive in FolderListingService.ListFixedDrives())
            thisPc.Children.Add(new NavNode(drive.Name, drive.FullPath));

        NavRoots.Add(thisPc);
    }

    [RelayCommand]
    private async Task NavigateToAsync(string path)
        => await NavigateToAsync(path, recordHistory: true);

    private async Task NavigateToAsync(string path, bool recordHistory)
    {
        if (recordHistory && !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(CurrentPath);
            _forward.Clear();
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        }

        CurrentPath = path;
        AddressBarText = path == ThisPcPath ? "This PC" : path;
        IsLoading = true;

        try
        {
            var entries = path == ThisPcPath
                ? FolderListingService.ListFixedDrives()
                : await FolderListingService.ListFolderAsync(path);

            Items.Clear();
            foreach (var entry in entries)
                Items.Add(new FileRow(entry));
        }
        catch (IOException)
        {
            // Path vanished/became inaccessible mid-navigation — leave the list as-is.
        }
        finally
        {
            IsLoading = false;
            GoUpCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void OpenItem(FileRow? row)
    {
        if (row is null) return;
        if (row.IsDirectory)
            _ = NavigateToAsync(row.FullPath, recordHistory: true);
        else
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.FullPath) { UseShellExecute = true });
    }

    private bool CanGoBack() => _back.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBackAsync()
    {
        if (_back.Count == 0) return;
        _forward.Push(CurrentPath);
        var target = _back.Pop();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        await NavigateToAsync(target, recordHistory: false);
    }

    private bool CanGoForward() => _forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task GoForwardAsync()
    {
        if (_forward.Count == 0) return;
        _back.Push(CurrentPath);
        var target = _forward.Pop();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        await NavigateToAsync(target, recordHistory: false);
    }

    private bool CanGoUp() => CurrentPath != ThisPcPath;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task GoUpAsync()
    {
        if (!CanGoUp()) return;
        var parent = Directory.GetParent(CurrentPath);
        await NavigateToAsync(parent?.FullName ?? ThisPcPath, recordHistory: true);
    }
}
