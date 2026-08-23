using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RoninExplorer.App.ViewModels;

/// <summary>
/// One row in the "New" dropdown — "Folder" plus whatever the registry's
/// ShellNew mechanism offers on this machine (see NewItemTemplateService).
/// Each entry carries its own creation action so the menu's ItemsSource
/// binding needs no per-type switch in the view.
/// </summary>
public sealed partial class NewMenuEntry(string displayName, Func<Task> createAction) : ObservableObject
{
    public string DisplayName { get; } = displayName;

    [RelayCommand]
    private Task Create() => createAction();
}
