using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
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

    [ObservableProperty]
    private string _statusMessage = string.Empty;

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

    // ── File operations (M2 bootstrap — System.IO + Recycle Bin + real Windows
    // clipboard, so cut/copy/paste round-trips with actual Explorer too) ──────

    private bool CanMutateCurrentFolder() => CurrentPath != ThisPcPath;

    public async Task NewFolderAsync()
    {
        if (!CanMutateCurrentFolder()) return;

        var created = BasicFileOperations.CreateFolder(CurrentPath);
        await NavigateToAsync(CurrentPath, recordHistory: false);

        var row = Items.FirstOrDefault(r => string.Equals(r.FullPath, created, StringComparison.OrdinalIgnoreCase));
        if (row is not null) BeginRename(row);
    }

    public void BeginRename(FileRow row)
    {
        row.EditName = row.Name;
        row.IsRenaming = true;
    }

    public async Task CommitRenameAsync(FileRow row)
    {
        row.IsRenaming = false;
        if (string.IsNullOrWhiteSpace(row.EditName) || row.EditName == row.Name)
            return;

        if (!BasicFileOperations.Rename(row.FullPath, row.EditName, out var error))
        {
            StatusMessage = error;
            return;
        }

        await NavigateToAsync(CurrentPath, recordHistory: false);
    }

    public void CancelRename(FileRow row) => row.IsRenaming = false;

    public Task RefreshAsync() => NavigateToAsync(CurrentPath, recordHistory: false);

    public async Task DeleteAsync(IReadOnlyList<FileRow> rows)
    {
        if (rows.Count == 0) return;

        foreach (var row in rows)
        {
            if (!RecycleBin.Send(row.FullPath, out var error))
                StatusMessage = error;
        }

        await NavigateToAsync(CurrentPath, recordHistory: false);
    }

    public void Copy(IReadOnlyList<FileRow> rows) => SetClipboard(rows, isCut: false);

    public void Cut(IReadOnlyList<FileRow> rows) => SetClipboard(rows, isCut: true);

    private static void SetClipboard(IReadOnlyList<FileRow> rows, bool isCut)
    {
        if (rows.Count == 0) return;

        var files = new StringCollection();
        files.AddRange([.. rows.Select(r => r.FullPath)]);

        var data = new DataObject();
        data.SetFileDropList(files);
        // "Preferred DropEffect" is the shell's own convention for cut-vs-copy
        // on the clipboard — setting it means pasting into a real Explorer
        // window after Cut moves the files instead of copying them, and
        // vice versa when pasting something copied/cut from Explorer here.
        var effect = new MemoryStream(BitConverter.GetBytes((int)(isCut ? DragDropEffects.Move : DragDropEffects.Copy)));
        data.SetData("Preferred DropEffect", effect);

        Clipboard.SetDataObject(data, copy: true);
    }

    public async Task PasteAsync()
    {
        if (!CanMutateCurrentFolder()) return;
        if (!Clipboard.ContainsFileDropList()) return;

        var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
        if (paths.Count == 0) return;

        var isMove = TryGetPreferredDropEffect(out var effect) && effect.HasFlag(DragDropEffects.Move);

        if (isMove)
            BasicFileOperations.MoveToFolder(paths, CurrentPath);
        else
            BasicFileOperations.CopyToFolder(paths, CurrentPath);

        await NavigateToAsync(CurrentPath, recordHistory: false);
    }

    private static bool TryGetPreferredDropEffect(out DragDropEffects effect)
    {
        effect = DragDropEffects.None;
        var data = Clipboard.GetDataObject();
        if (data is null || !data.GetDataPresent("Preferred DropEffect")) return false;

        if (data.GetData("Preferred DropEffect") is not MemoryStream stream) return false;
        var bytes = new byte[4];
        if (stream.Read(bytes, 0, 4) < 4) return false;

        effect = (DragDropEffects)BitConverter.ToInt32(bytes, 0);
        return true;
    }
}
