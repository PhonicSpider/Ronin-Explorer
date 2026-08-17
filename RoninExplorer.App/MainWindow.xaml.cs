using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        DataContext = _viewModel;
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

    // ── Keyboard shortcuts (hardcoded for M2 — a configurable KeybindService is M6) ──

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Let an active rename TextBox (or any other TextBox) handle its own
        // Delete/Ctrl+C/etc. instead of the app-level shortcuts stealing them.
        if (e.OriginalSource is TextBox) return;

        var selected = SelectedRows();
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (e.Key)
        {
            case Key.F2 when selected.Count == 1:
                _viewModel.BeginRename(selected[0]);
                e.Handled = true;
                break;
            case Key.Delete when selected.Count > 0:
                await _viewModel.DeleteAsync(selected);
                e.Handled = true;
                break;
            case Key.C when ctrl && selected.Count > 0:
                _viewModel.Copy(selected);
                e.Handled = true;
                break;
            case Key.X when ctrl && selected.Count > 0:
                _viewModel.Cut(selected);
                e.Handled = true;
                break;
            case Key.V when ctrl:
                await _viewModel.PasteAsync();
                e.Handled = true;
                break;
            case Key.N when ctrl && shift:
                await _viewModel.NewFolderAsync();
                e.Handled = true;
                break;
            case Key.F5:
                await _viewModel.RefreshAsync();
                e.Handled = true;
                break;
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
}
