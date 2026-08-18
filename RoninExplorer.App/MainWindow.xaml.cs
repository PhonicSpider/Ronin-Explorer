using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoninExplorer.App.Services;
using RoninExplorer.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
// The file list and rename box are plain WPF controls (not ui: prefixed in
// XAML), but WPF-UI also ships types with the same short names — disambiguate
// in favor of the standard controls actually used in MainWindow.xaml.
using TextBox = System.Windows.Controls.TextBox;
using ListViewItem = System.Windows.Controls.ListViewItem;

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

        // Forces early creation of the window's HWND so FileOperationService
        // has a real owner to hang its native progress/conflict dialogs off of.
        _viewModel.OwnerHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        ThemeService.ApplySkin(ThemeService.LoadOrDefault(ThemeService.LoadLastUsedSkinName()));
        BuildKeybindActions();
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

    // ── View modes and column sorting (M3) ──────────────────────────────────

    private void ViewModeDetails_Click(object sender, RoutedEventArgs e) => _viewModel.ViewMode = FileListViewMode.Details;

    private void ViewModeLargeIcons_Click(object sender, RoutedEventArgs e) => _viewModel.ViewMode = FileListViewMode.LargeIcons;

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

    // ── Theming (M6) ─────────────────────────────────────────────────────────

    private void Theme_Click(object sender, RoutedEventArgs e)
        => new ThemeSettingsWindow { Owner = this }.ShowDialog();

    // ── Keybind rebinding UI (M6) ────────────────────────────────────────────

    private void Keybinds_Click(object sender, RoutedEventArgs e)
    {
        new KeybindSettingsWindow { Owner = this }.ShowDialog();
        _keybinds = KeybindService.LoadEffectiveMap(); // pick up any rebinds made in the dialog
    }
}
