using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RoninExplorer.App.Services;
using RoninExplorer.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
// The file list and rename box are plain WPF controls (not ui: prefixed in
// XAML), but WPF-UI also ships types with the same short names — disambiguate
// in favor of the standard controls actually used in MainWindow.xaml.
using TextBox = System.Windows.Controls.TextBox;
using ListViewItem = System.Windows.Controls.ListViewItem;
using TreeViewItem = System.Windows.Controls.TreeViewItem;
using Button = System.Windows.Controls.Button;

namespace RoninExplorer.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel = new();
    private Dictionary<string, string> _keybinds = KeybindService.LoadEffectiveMap();
    private Dictionary<string, Func<Task>> _keybindActions = null!;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        DataContext = _viewModel;
        _viewModel.CloseWindowRequested += () => Close();

        // Forces early creation of the window's HWND so FileOperationService
        // has a real owner to hang its native progress/conflict dialogs off of.
        _viewModel.OwnerHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        ThemeService.ApplySkin(ThemeService.LoadOrDefault(ThemeService.LoadLastUsedSkinName()));
        BuildKeybindActions();

        // "New window" passes the current folder as an argument so the new
        // instance opens where you were, matching Explorer's own behavior.
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
            _ = _viewModel.NavigateToTypedPathAsync(args[1]);
    }

    private void BuildKeybindActions()
    {
        _keybindActions = new Dictionary<string, Func<Task>>
        {
            ["Rename"] = () => { if (SelectedRows() is [var row]) _viewModel.BeginRename(row); return Task.CompletedTask; },
            ["Delete"] = () => SelectedRows() is { Count: > 0 } rows ? _viewModel.DeleteAsync(rows) : Task.CompletedTask,
            ["Copy"] = () => { if (SelectedRows() is { Count: > 0 } rows) _viewModel.Copy(rows); return Task.CompletedTask; },
            ["Cut"] = () => { if (SelectedRows() is { Count: > 0 } rows) _viewModel.Cut(rows); return Task.CompletedTask; },
            ["Paste"] = () => _viewModel.PasteAsync(),
            ["NewFolder"] = () => _viewModel.NewFolderAsync(),
            ["Refresh"] = () => _viewModel.RefreshAsync(),
        };
    }

    private void NavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is NavNode { Path: { } path })
            _viewModel.NavigateToCommand.Execute(path);
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row)
            _viewModel.OpenItemCommand.Execute(row);
    }

    private List<FileRow> SelectedRows() => FileList.SelectedItems.Cast<FileRow>().ToList();

    // ── Keyboard shortcuts (M6 — user-rebindable via KeybindService/KeybindSettingsWindow) ──

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Let an active rename TextBox (or any other TextBox) handle its own
        // Delete/Ctrl+C/etc. instead of the app-level shortcuts stealing them.
        if (e.OriginalSource is TextBox) return;

        // Select All and tab shortcuts are fixed (like Explorer's/a browser's), not part of the rebindable keybind map.
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FileList.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.NewTab();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.CloseTab(_viewModel.ActiveTab);
            e.Handled = true;
            return;
        }

        var gesture = FormatGesture(e.Key, Keyboard.Modifiers);
        if (gesture is null) return;

        var actionId = _keybinds.FirstOrDefault(kv => string.Equals(kv.Value, gesture, StringComparison.OrdinalIgnoreCase)).Key;
        if (actionId is null || !_keybindActions.TryGetValue(actionId, out var action)) return;

        e.Handled = true;
        await action();
    }

    private static string? FormatGesture(Key key, ModifierKeys modifiers)
    {
        try
        {
            var kg = new KeyGesture(key, modifiers);
            return new KeyGestureConverter().ConvertToString(null, CultureInfo.InvariantCulture, kg);
        }
        catch (ArgumentException)
        {
            return null; // not a valid standalone/modifier gesture (e.g. a bare letter key) — not one of our shortcuts
        }
    }

    // ── Inline rename ────────────────────────────────────────────────────────

    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        box.Focus();
        box.SelectAll();
    }

    private async void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileRow row }) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _viewModel.CommitRenameAsync(row);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _viewModel.CancelRename(row);
        }
    }

    private async void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FileRow { IsRenaming: true } row })
            await _viewModel.CommitRenameAsync(row);
    }

    // ── Context menus ────────────────────────────────────────────────────────

    private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem { DataContext: FileRow row } item) return;
        if (!FileList.SelectedItems.Contains(row))
        {
            FileList.SelectedItems.Clear();
            FileList.SelectedItems.Add(row);
        }
    }

    private void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row)
            _viewModel.OpenItemCommand.Execute(row);
    }

    private void ContextCut_Click(object sender, RoutedEventArgs e) => _viewModel.Cut(SelectedRows());

    private void ContextCopy_Click(object sender, RoutedEventArgs e) => _viewModel.Copy(SelectedRows());

    private async void ContextPaste_Click(object sender, RoutedEventArgs e) => await _viewModel.PasteAsync();

    private async void EmptyPaste_Click(object sender, RoutedEventArgs e) => await _viewModel.PasteAsync();

    private void ContextRename_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row)
            _viewModel.BeginRename(row);
    }

    private async void ContextDelete_Click(object sender, RoutedEventArgs e) => await _viewModel.DeleteAsync(SelectedRows());

    private void ContextProperties_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row)
            MainViewModel.ShowProperties(row);
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e) => await _viewModel.NewFolderAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();

    // ── Favorites/pinned folders ─────────────────────────────────────────────

    private void PinToFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is FileRow { IsDirectory: true } row)
            _viewModel.PinFolder(row.FullPath);
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NavNode node })
            _viewModel.UnpinFolder(node);
    }

    private void FavoriteItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NavNode { Path: { } path } })
            _viewModel.NavigateToCommand.Execute(path);
    }

    // ── Extras: zip, terminal, new window (Explorer parity pass) ───────────

    private async void ExtractZip_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row)
            await _viewModel.ExtractZipAsync(row);
    }

    private async void CompressSelection_Click(object sender, RoutedEventArgs e) => await _viewModel.CompressSelectionAsync(SelectedRows());

    private void OpenTerminal_Click(object sender, RoutedEventArgs e) => _viewModel.OpenTerminalHere();

    private void NewWindow_Click(object sender, RoutedEventArgs e) => _viewModel.OpenNewWindow();

    // ── Tabs (M8) ────────────────────────────────────────────────────────────

    private void NewTab_Click(object sender, RoutedEventArgs e) => _viewModel.NewTab();

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabState tab })
            _viewModel.CloseTab(tab);
    }

    private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Don't switch tabs when the click was on the close ("x") button —
        // its own Click handler (above) already handled that case.
        if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) is not null) return;
        if (sender is FrameworkElement { DataContext: TabState tab })
            _viewModel.SwitchToTab(tab);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    // ── View modes and column sorting (M3) ──────────────────────────────────

    private void ViewModeDetails_Click(object sender, RoutedEventArgs e) => _viewModel.ViewMode = FileListViewMode.Details;

    private void ViewModeLargeIcons_Click(object sender, RoutedEventArgs e) => _viewModel.ViewMode = FileListViewMode.LargeIcons;

    // ── "New" and "View options" dropdowns ──────────────────────────────────

    /// <summary>Opens whichever ContextMenu is attached to the clicked element — shared by both hamburger dropdown buttons (New / View options).</summary>
    private void OpenDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }

    private async void NewTextDocument_Click(object sender, RoutedEventArgs e) => await _viewModel.NewFileAsync("New Text Document.txt");

    private void SortByName_Click(object sender, RoutedEventArgs e) => _viewModel.SortBy(FileSortColumn.Name);

    private void SortByDate_Click(object sender, RoutedEventArgs e) => _viewModel.SortBy(FileSortColumn.DateModified);

    private void SortByType_Click(object sender, RoutedEventArgs e) => _viewModel.SortBy(FileSortColumn.Type);

    private void SortBySize_Click(object sender, RoutedEventArgs e) => _viewModel.SortBy(FileSortColumn.Size);

    private void SortAscending_Click(object sender, RoutedEventArgs e) => _viewModel.SetSortDirection(ascending: true);

    private void SortDescending_Click(object sender, RoutedEventArgs e) => _viewModel.SetSortDirection(ascending: false);

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string tag }) return;
        if (Enum.TryParse<FileSortColumn>(tag, out var column))
            _viewModel.SortBy(column);
    }

    // ── Search (M4) ──────────────────────────────────────────────────────────

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _viewModel.SearchAsync();
    }

    // ── Details/Tools panel (M5) ────────────────────────────────────────────

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.UpdateSelection(SelectedRows());

    private void PanelModeDetails_Click(object sender, RoutedEventArgs e) => _viewModel.PanelMode = DetailsToolsMode.Details;

    private void PanelModeTools_Click(object sender, RoutedEventArgs e) => _viewModel.PanelMode = DetailsToolsMode.Tools;

    // Plays a video preview for ~5 seconds then pauses, as asked for — WPF's
    // MediaElement has no built-in "play N seconds" option, so this is a
    // manual DispatcherTimer. Stopped (not just paused) when the preview goes
    // invisible so switching away from a video can't leave audio playing.
    private DispatcherTimer? _videoPreviewTimer;

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement media) return;
        _videoPreviewTimer?.Stop();
        media.Position = TimeSpan.Zero;
        media.Play();
        _videoPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _videoPreviewTimer.Tick += (_, _) =>
        {
            media.Pause();
            _videoPreviewTimer!.Stop();
        };
        _videoPreviewTimer.Start();
    }

    private void VideoPreview_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not MediaElement media) return;
        if (media.IsVisible) return;

        _videoPreviewTimer?.Stop();
        media.Stop();
    }

    private async void CalculateFolderSize_Click(object sender, RoutedEventArgs e) => await _viewModel.CalculateFolderSizeAsync();

    private async void CalculateHash_Click(object sender, RoutedEventArgs e) => await _viewModel.CalculateHashAsync();

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e) => await _viewModel.FindDuplicatesInCurrentFolderAsync();

    private async void DeleteDuplicateGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoninExplorer.Core.Engine.DuplicateFinder.DuplicateGroup group })
            await _viewModel.DeleteDuplicateGroupAsync(group);
    }

    private async void ScanCleanup_Click(object sender, RoutedEventArgs e) => await _viewModel.ScanCleanupAsync();

    private async void DeleteCleanupResults_Click(object sender, RoutedEventArgs e) => await _viewModel.DeleteCleanupResultsAsync();

    // ── Editable address bar / hidden-files toggle (Explorer parity pass) ────

    private async void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _viewModel.NavigateToTypedPathAsync(AddressBar.Text);
        Keyboard.ClearFocus();
    }

    // ── Theming (M6) ─────────────────────────────────────────────────────────

    private void Theme_Click(object sender, RoutedEventArgs e)
        => new ThemeSettingsWindow { Owner = this }.ShowDialog();

    // ── Keybind rebinding UI (M6) ────────────────────────────────────────────

    private void Keybinds_Click(object sender, RoutedEventArgs e)
    {
        new KeybindSettingsWindow { Owner = this }.ShowDialog();
        _keybinds = KeybindService.LoadEffectiveMap(); // pick up any rebinds made in the dialog
    }

    // ── Drag and drop (Explorer parity pass) ────────────────────────────────
    // Uses DataFormats.FileDrop (CF_HDROP) — the same standard format
    // Clipboard cut/copy/paste already uses — so dragging interoperates with
    // real Explorer and other apps in both directions, not just within this app.

    private Point _dragStartPoint;

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPoint = e.GetPosition(null);

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var rows = SelectedRows();
        if (rows.Count == 0 || rows.Any(r => r.IsRenaming)) return;

        var files = new System.Collections.Specialized.StringCollection();
        files.AddRange([.. rows.Select(r => r.FullPath)]);
        var data = new DataObject();
        data.SetFileDropList(files);

        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? ResolveDropEffect(e, ResolveFileListDropTarget(e)) : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var destFolder = ResolveFileListDropTarget(e);
        var isMove = ResolveDropEffect(e, destFolder) == DragDropEffects.Move;
        await _viewModel.DropFilesAsync(paths, destFolder, isMove);
    }

    /// <summary>The folder a drop should land in: a folder row under the cursor, or the current folder when dropped on empty space.</summary>
    private string ResolveFileListDropTarget(DragEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not ListViewItem)
            source = VisualTreeHelper.GetParent(source);

        return (source as ListViewItem)?.DataContext is FileRow { IsDirectory: true } row
            ? row.FullPath
            : _viewModel.CurrentPath;
    }

    private DragDropEffects ResolveDropEffect(DragEventArgs e, string destFolder)
    {
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)) return DragDropEffects.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey)) return DragDropEffects.Move;

        // Default, like Explorer: move within the same drive, copy across drives.
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            var destRoot = Path.GetPathRoot(destFolder);
            var srcRoot = Path.GetPathRoot(paths[0]);
            return string.Equals(destRoot, srcRoot, StringComparison.OrdinalIgnoreCase) ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        return DragDropEffects.Copy;
    }

    private void NavTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && ResolveNavDropTarget(e) is not null
            ? ResolveDropEffect(e, ResolveNavDropTarget(e)!)
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void NavTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var destFolder = ResolveNavDropTarget(e);
        if (destFolder is null) return;

        var isMove = ResolveDropEffect(e, destFolder) == DragDropEffects.Move;
        await _viewModel.DropFilesAsync(paths, destFolder, isMove);
    }

    private static string? ResolveNavDropTarget(DragEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not TreeViewItem)
            source = VisualTreeHelper.GetParent(source);

        return (source as TreeViewItem)?.DataContext is NavNode { Path: { Length: > 0 } path } ? path : null;
    }
}
