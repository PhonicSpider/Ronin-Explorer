using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninExplorer.App.Services;
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

    /// <summary>Set by MainWindow at startup — the native window handle FileOperationService hangs its progress/conflict dialogs off of.</summary>
    public IntPtr OwnerHandle { get; set; }

    [ObservableProperty]
    private FileListViewMode _viewMode = FileListViewMode.Details;

    [ObservableProperty]
    private FileSortColumn _sortColumn = FileSortColumn.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _showHiddenFiles;

    public string ItemCountDisplay => Items.Count == 1 ? "1 item" : $"{Items.Count} items";

    // ── Details/Tools panel (M5) ────────────────────────────────────────────

    [ObservableProperty]
    private DetailsToolsMode _panelMode = DetailsToolsMode.Details;

    [ObservableProperty]
    private FileRow? _detailsSingleItem;

    [ObservableProperty]
    private int _detailsSelectedCount;

    [ObservableProperty]
    private long _detailsSelectedTotalSize;

    [ObservableProperty]
    private string _detailsFolderSizeDisplay = string.Empty;

    [ObservableProperty]
    private bool _isCalculatingFolderSize;

    [ObservableProperty]
    private string _detailsHashDisplay = string.Empty;

    [ObservableProperty]
    private bool _isCalculatingHash;

    [ObservableProperty]
    private bool _isFindingDuplicates;

    [ObservableProperty]
    private string _cleanupPattern = "*.tmp";

    [ObservableProperty]
    private int _cleanupOlderThanDays = 30;

    [ObservableProperty]
    private bool _isScanningCleanup;

    public ObservableCollection<DuplicateFinder.DuplicateGroup> DuplicateGroups { get; } = [];
    public ObservableCollection<SearchHit> CleanupResults { get; } = [];

    public string DetailsSelectedTotalSizeDisplay => FileSystemHelpers.FormatBytes(DetailsSelectedTotalSize);

    private readonly VolumeIndexManager _volumeIndex = new();
    private readonly SearchIndexEngine _searchEngine;

    public ObservableCollection<FileRow> Items { get; } = [];
    public ObservableCollection<NavNode> NavRoots { get; } = [];

    // ── Favorites/pinned folders ─────────────────────────────────────────────

    public ObservableCollection<NavNode> Favorites { get; } = [];

    private void LoadFavorites()
    {
        foreach (var path in PinnedFoldersService.Load())
            Favorites.Add(new NavNode(Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, path));
    }

    public bool IsPinned(string path) => Favorites.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    public void PinFolder(string path)
    {
        if (IsPinned(path)) return;
        Favorites.Add(new NavNode(Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, path));
        PinnedFoldersService.Save(Favorites.Select(f => f.Path!));
    }

    public void UnpinFolder(NavNode node)
    {
        Favorites.Remove(node);
        PinnedFoldersService.Save(Favorites.Select(f => f.Path!));
    }

    // ── Tabs (M8) ────────────────────────────────────────────────────────────

    public ObservableCollection<TabState> Tabs { get; } = [];

    [ObservableProperty]
    private TabState _activeTab = null!;

    /// <summary>Raised when the last remaining tab is closed — MainWindow closes the window in response, matching a browser/Explorer tab strip.</summary>
    public event Action? CloseWindowRequested;

    public MainViewModel()
    {
        _searchEngine = new SearchIndexEngine(_volumeIndex);
        BuildNavPane();
        LoadFavorites();

        var initialTab = new TabState(ThisPcPath);
        Tabs.Add(initialTab);
        _activeTab = initialTab;

        _ = NavigateToAsync(ThisPcPath, recordHistory: false);
        // Elevation-gated inside — a no-op (instant) when not running as admin,
        // so this never blocks or delays startup for the common case.
        _ = _volumeIndex.BuildAllAsync();
    }

    public void NewTab()
    {
        var tab = new TabState(CurrentPath);
        Tabs.Add(tab);
        SwitchToTab(tab);
    }

    public void CloseTab(TabState tab)
    {
        if (Tabs.Count <= 1)
        {
            CloseWindowRequested?.Invoke();
            return;
        }

        var closingActive = tab == ActiveTab;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (closingActive)
            SwitchToTab(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    public void SwitchToTab(TabState tab)
    {
        if (tab == ActiveTab) return;

        // Snapshot the outgoing tab's history before handing the shared
        // back/forward stacks over to the incoming tab.
        ActiveTab.Back.Clear();
        foreach (var p in _back.Reverse()) ActiveTab.Back.Push(p);
        ActiveTab.Forward.Clear();
        foreach (var p in _forward.Reverse()) ActiveTab.Forward.Push(p);

        ActiveTab = tab;
        _back.Clear();
        foreach (var p in tab.Back.Reverse()) _back.Push(p);
        _forward.Clear();
        foreach (var p in tab.Forward.Reverse()) _forward.Push(p);

        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();

        _ = NavigateToAsync(tab.CurrentPath, recordHistory: false);
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
        if (ActiveTab is not null) ActiveTab.CurrentPath = path; // keeps the tab strip's title live as you browse
        IsLoading = true;

        try
        {
            var entries = path == ThisPcPath
                ? FolderListingService.ListFixedDrives()
                : await FolderListingService.ListFolderAsync(path, ShowHiddenFiles);

            Items.Clear();
            foreach (var entry in entries)
                Items.Add(new FileRow(entry));
            ApplySort();
            OnPropertyChanged(nameof(ItemCountDisplay));
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

    /// <summary>Navigates to a path typed directly into the (now-editable) address bar. Explorer-like: silently no-ops rather than erroring loudly on a bad path.</summary>
    public async Task NavigateToTypedPathAsync(string typedPath)
    {
        var path = typedPath.Trim();
        if (Directory.Exists(path))
            await NavigateToAsync(path, recordHistory: true);
        else
            StatusMessage = $"\"{path}\" could not be found.";
    }

    /// <summary>CommunityToolkit-generated hook — fires whenever ShowHiddenFiles changes, including via the toolbar toggle button's two-way binding.</summary>
    partial void OnShowHiddenFilesChanged(bool value) => _ = NavigateToAsync(CurrentPath, recordHistory: false);

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

    /// <summary>Launches another instance of this app at the current folder — Explorer's "New window".</summary>
    public void OpenNewWindow()
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (exe is null) return;
        var startPath = CurrentPath == ThisPcPath ? string.Empty : CurrentPath;
        System.Diagnostics.Process.Start(exe, startPath);
    }

    /// <summary>Opens a terminal (Windows Terminal, falling back to cmd) at the current folder — Explorer's "Open in Terminal".</summary>
    public void OpenTerminalHere()
    {
        if (!CanMutateCurrentFolder()) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe")
            {
                ArgumentList = { "-d", CurrentPath },
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = CurrentPath,
                UseShellExecute = true,
            });
        }
    }

    public async Task ExtractZipAsync(FileRow row)
    {
        if (!string.Equals(row.Entry.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Only .zip files can be extracted.";
            return;
        }
        try
        {
            ArchiveService.ExtractZip(row.FullPath);
            await NavigateToAsync(CurrentPath, recordHistory: false);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    public async Task CompressSelectionAsync(IReadOnlyList<FileRow> rows)
    {
        if (rows.Count == 0 || !CanMutateCurrentFolder()) return;
        try
        {
            ArchiveService.CompressToZip([.. rows.Select(r => r.FullPath)], CurrentPath);
            await NavigateToAsync(CurrentPath, recordHistory: false);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    // ── Sorting — folders always group before files, matching Explorer, with
    // the clicked column as the secondary key within each group ──────────────

    public void SortBy(FileSortColumn column)
    {
        if (SortColumn == column) SortAscending = !SortAscending;
        else { SortColumn = column; SortAscending = true; }
        ApplySort();
    }

    /// <summary>Sets the sort direction directly (the View-options dropdown's explicit Ascending/Descending entries), leaving the sort column unchanged.</summary>
    public void SetSortDirection(bool ascending)
    {
        if (SortAscending == ascending) return;
        SortAscending = ascending;
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

    // ── File operations (M7 — IFileOperation for the native progress dialog,
    // conflict-resolution UI, and shell undo-stack entry; New Folder stays on
    // BasicFileOperations since instant creation gets nothing from the native
    // dialog) ──────────────────────────────────────────────────────────────

    private bool CanMutateCurrentFolder() => CurrentPath != ThisPcPath;

    public async Task NewFolderAsync()
    {
        if (!CanMutateCurrentFolder()) return;

        var created = BasicFileOperations.CreateFolder(CurrentPath);
        await NavigateToAsync(CurrentPath, recordHistory: false);

        var row = Items.FirstOrDefault(r => string.Equals(r.FullPath, created, StringComparison.OrdinalIgnoreCase));
        if (row is not null) BeginRename(row);
    }

    /// <summary>Creates a new empty file with the given name (e.g. "New Text Document.txt") and enters inline rename — the "New" dropdown's non-folder options.</summary>
    public async Task NewFileAsync(string desiredName)
    {
        if (!CanMutateCurrentFolder()) return;

        var created = BasicFileOperations.CreateFile(CurrentPath, desiredName);
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

        var result = FileOperationService.RenameItem(row.FullPath, row.EditName, OwnerHandle);
        if (!result.Success)
        {
            StatusMessage = result.Error ?? string.Empty;
            return;
        }

        await NavigateToAsync(CurrentPath, recordHistory: false);
    }

    public void CancelRename(FileRow row) => row.IsRenaming = false;

    public Task RefreshAsync() => NavigateToAsync(CurrentPath, recordHistory: false);

    public async Task DeleteAsync(IReadOnlyList<FileRow> rows)
    {
        if (rows.Count == 0) return;

        var result = FileOperationService.DeleteItems(rows.Select(r => r.FullPath), OwnerHandle);
        if (!result.Success && !result.WasAborted)
            StatusMessage = result.Error ?? string.Empty;

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

    /// <summary>Drag-and-drop equivalent of paste — from either this app's own drag source or an external one (real Explorer, desktop), since both use the standard CF_HDROP format.</summary>
    public async Task DropFilesAsync(IReadOnlyList<string> sourcePaths, string destFolder, bool isMove)
    {
        // Dropping items back into the folder they already came from is a no-op, not an error.
        var paths = sourcePaths.Where(p => !string.Equals(Path.GetDirectoryName(p), destFolder, StringComparison.OrdinalIgnoreCase)).ToList();
        if (paths.Count == 0) return;

        var result = isMove
            ? FileOperationService.MoveItems(paths, destFolder, OwnerHandle)
            : FileOperationService.CopyItems(paths, destFolder, OwnerHandle);

        if (!result.Success && !result.WasAborted)
            StatusMessage = result.Error ?? string.Empty;

        if (string.Equals(destFolder, CurrentPath, StringComparison.OrdinalIgnoreCase))
            await NavigateToAsync(CurrentPath, recordHistory: false);
    }

    public async Task PasteAsync()
    {
        if (!CanMutateCurrentFolder()) return;
        if (!Clipboard.ContainsFileDropList()) return;

        var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
        if (paths.Count == 0) return;

        var isMove = TryGetPreferredDropEffect(out var effect) && effect.HasFlag(DragDropEffects.Move);

        var result = isMove
            ? FileOperationService.MoveItems(paths, CurrentPath, OwnerHandle)
            : FileOperationService.CopyItems(paths, CurrentPath, OwnerHandle);

        if (!result.Success && !result.WasAborted)
            StatusMessage = result.Error ?? string.Empty;

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
            OnPropertyChanged(nameof(ItemCountDisplay));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Details/Tools panel (M5) ────────────────────────────────────────────
    // Details: extended metadata beyond stock Explorer, computed on demand
    // (recursive folder size, SHA-256 hash) rather than eagerly for every
    // selection change, since both can be slow on a big folder/file. Tools:
    // selection/folder-scoped actions reusing DuplicateFinder/CleanupScanner.

    public void UpdateSelection(IReadOnlyList<FileRow> rows)
    {
        DetailsSelectedCount = rows.Count;
        DetailsSelectedTotalSize = rows.Sum(r => r.Entry.SizeBytes);
        DetailsSingleItem = rows.Count == 1 ? rows[0] : null;
        DetailsFolderSizeDisplay = string.Empty;
        DetailsHashDisplay = string.Empty;
        OnPropertyChanged(nameof(DetailsSelectedTotalSizeDisplay));
    }

    public async Task CalculateFolderSizeAsync()
    {
        if (DetailsSingleItem is not { IsDirectory: true } row) return;
        IsCalculatingFolderSize = true;
        try
        {
            var size = await FolderListingService.GetFolderSizeAsync(row.FullPath);
            DetailsFolderSizeDisplay = FileSystemHelpers.FormatBytes(size);
        }
        finally
        {
            IsCalculatingFolderSize = false;
        }
    }

    public async Task CalculateHashAsync()
    {
        if (DetailsSingleItem is not { IsDirectory: false } row) return;
        IsCalculatingHash = true;
        try
        {
            DetailsHashDisplay = await Task.Run(() =>
            {
                using var stream = File.OpenRead(row.FullPath);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            });
        }
        catch (Exception ex)
        {
            DetailsHashDisplay = $"Error: {ex.Message}";
        }
        finally
        {
            IsCalculatingHash = false;
        }
    }

    public async Task FindDuplicatesInCurrentFolderAsync()
    {
        if (!CanMutateCurrentFolder()) return;
        IsFindingDuplicates = true;
        try
        {
            var files = await FolderListingService.ListFilesRecursiveAsync(CurrentPath);
            var groups = await DuplicateFinder.FindDuplicatesAsync(files);
            DuplicateGroups.Clear();
            foreach (var g in groups) DuplicateGroups.Add(g);
        }
        finally
        {
            IsFindingDuplicates = false;
        }
    }

    /// <summary>Recycles every file in the group except the first (kept as the surviving copy).</summary>
    public async Task DeleteDuplicateGroupAsync(DuplicateFinder.DuplicateGroup group)
    {
        var result = FileOperationService.DeleteItems(group.Files.Skip(1).Select(f => f.FullPath), OwnerHandle);
        if (!result.Success && !result.WasAborted)
            StatusMessage = result.Error ?? string.Empty;

        await FindDuplicatesInCurrentFolderAsync();
        await RefreshAsync();
    }

    public async Task ScanCleanupAsync()
    {
        if (!CanMutateCurrentFolder()) return;
        IsScanningCleanup = true;
        try
        {
            var results = await CleanupScanner.FindAsync(CurrentPath, CleanupPattern, CleanupOlderThanDays, recurse: true);
            CleanupResults.Clear();
            foreach (var hit in results) CleanupResults.Add(hit);
        }
        finally
        {
            IsScanningCleanup = false;
        }
    }

    public async Task DeleteCleanupResultsAsync()
    {
        if (CleanupResults.Count == 0) return;

        var result = FileOperationService.DeleteItems(CleanupResults.Select(h => h.FullPath), OwnerHandle);
        if (result.Success)
            CleanupResults.Clear();
        else if (!result.WasAborted)
            StatusMessage = result.Error ?? string.Empty;

        await RefreshAsync();
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
