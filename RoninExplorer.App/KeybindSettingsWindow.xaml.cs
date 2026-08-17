using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using RoninExplorer.App.Services;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace RoninExplorer.App;

public partial class KeybindSettingsWindow : FluentWindow
{
    private readonly Dictionary<string, string> _map;
    private readonly List<KeybindRow> _rows;
    private string? _capturingActionId;

    public KeybindSettingsWindow()
    {
        InitializeComponent();
        _map = KeybindService.LoadEffectiveMap();
        _rows = [.. _map.Select(kv => new KeybindRow(kv.Key, kv.Value))];
        ActionsList.ItemsSource = _rows;
    }

    private void Rebind_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }) return;
        _capturingActionId = actionId;
        StatusText.Text = $"Press a key for \"{actionId}\"... (Esc to cancel)";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingActionId is null) return;
        e.Handled = true;

        var actionId = _capturingActionId;
        _capturingActionId = null;
        StatusText.Text = "Click Rebind, then press a key.";

        if (e.Key == Key.Escape) return;

        var gesture = FormatGesture(e.Key, Keyboard.Modifiers);
        if (gesture is null)
        {
            StatusText.Text = "That key can't be used as a shortcut on its own.";
            return;
        }

        _map[actionId] = gesture;
        var row = _rows.First(r => r.ActionId == actionId);
        row.Gesture = gesture;
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
            return null; // not a valid standalone/modifier combo (e.g. a bare letter key)
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            var def = KeybindService.Defaults[row.ActionId];
            row.Gesture = def;
            _map[row.ActionId] = def;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        KeybindService.SaveEffectiveMap(_map);
        Close();
    }
}

public partial class KeybindRow(string actionId, string gesture) : ObservableObject
{
    public string ActionId { get; } = actionId;

    [ObservableProperty]
    private string _gesture = gesture;
}
