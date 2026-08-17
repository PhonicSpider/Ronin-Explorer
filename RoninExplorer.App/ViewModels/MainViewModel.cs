using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninExplorer.Core.Engine;
using RoninExplorer.Core.Models;

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

    [ObservableProperty]
    private FileListViewMode _viewMode = FileListViewMode.Details;

    [ObservableProperty]
    private FileSortColumn _sortColumn = FileSortColumn.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    private readonly VolumeIndexManager _volumeIndex = new();
    private readonly SearchIndexEngine _searchEngine;

    public ObservableCollection<FileRow> Items { get; } = [];
    public ObservableCollection<NavNode> NavRoots { get; } = [];

    public MainViewModel()
    {
        _searchEngine = new SearchIndexEngine(_volumeIndex);
        BuildNavPane();
        _ = NavigateToAsync(ThisPcPath, recordHistory: false);
        // Elevation-gated inside — a no-op (instant) when not running as admin,
        // so this never blocks or delays startup for the common case.
        _ = _volumeIndex.BuildAllAsync();
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
        SearchQuery = string.Empty; // leaving search results (if any) for a normal folder listing

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
            ApplySort();
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

    /// <summary>Opens the native Windows Properties dialog for a single item — exactly what Explorer's own "Properties" does.</summary>
    public static void ShowProperties(FileRow row)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.FullPath)
        {
            UseShellExecute = true,
            Verb = "properties",
        });

    // ── Sorting — folders always group before files, matching Explorer, with
    // the clicked column as the secondary key within each group ──────────────

    public void SortBy(FileSortColumn column)
    {
        if (SortColumn == column) SortAscending = !SortAscending;
        else { SortColumn = column; SortAscending = true; }
        ApplySort();
    }

    private void ApplySort()
    {
        IOrderedEnumerable<FileRow> ordered = SortColumn switch
        {
            FileSortColumn.DateModified => SortAscending
                ? Items.OrderByDescending(r => r.IsDirectory).ThenBy(r => r.DateModified)
                : Items.OrderByDescending(r => r.IsDirectory).ThenByDescending(r => r.DateModified),
            FileSortColumn.Type => SortAscending
                ? Items.OrderByDescending(r => r.IsDirectory).ThenBy(r => r.FileType, StringComparer.OrdinalIgnoreCase)
                : Items.OrderByDescending(r => r.IsDirectory).ThenByDescending(r => r.FileType, StringComparer.OrdinalIgnoreCase),
            FileSortColumn.Size => SortAscending
                ? Items.OrderByDescending(r => r.IsDirectory).ThenBy(r => r.Entry.SizeBytes)
                : Items.OrderByDescending(r => r.IsDirectory).ThenByDescending(r => r.Entry.SizeBytes),
            _ => SortAscending
                ? Items.OrderByDescending(r => r.IsDirectory).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                : Items.OrderByDescending(r => r.IsDirectory).ThenByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase),
        };

        var reordered = ordered.ToList();
        Items.Clear();
        foreach (var row in reordered) Items.Add(row);
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

    // ── Search (M4) — instant results from the MFT index when elevated and
    // available, falling back to LiveWalkSearchEngine otherwise ────────────

    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await NavigateToAsync(CurrentPath, recordHistory: false);
            return;
        }

        var query = SearchQuery; // NavigateToAsync (called on empty query) would otherwise clear it out from under us
        IsLoading = true;
        try
        {
            var hits = await _searchEngine.SearchAsync(query);

            Items.Clear();
            foreach (var hit in hits)
                Items.Add(new FileRow(new FileSystemEntry
                {
                    Name = hit.Name,
                    FullPath = hit.FullPath,
                    IsDirectory = hit.IsDirectory,
                    SizeBytes = hit.SizeBytes,
                    DateModified = hit.DateModified,
                    Extension = hit.IsDirectory ? string.Empty : Path.GetExtension(hit.Name),
                }));

            AddressBarText = $"Search results for \"{query}\"" + (_volumeIndex.HasAnyIndex ? "" : " (not elevated — full-disk index unavailable, searched live instead)");
            ApplySort();
        }
        finally
        {
            IsLoading = false;
        }
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
