using System.Windows;
using System.Windows.Input;
using RoninExplorer.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

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
}
