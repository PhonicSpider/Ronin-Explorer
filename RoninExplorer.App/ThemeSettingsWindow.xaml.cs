using System.Windows;
using Microsoft.Win32;
using RoninExplorer.App.Models;
using RoninExplorer.App.Services;
using Wpf.Ui.Controls;

namespace RoninExplorer.App;

public partial class ThemeSettingsWindow : FluentWindow
{
    private SkinDefinition _skin;

    public ThemeSettingsWindow()
    {
        InitializeComponent();
        _skin = ThemeService.LoadOrDefault();
        LoadIntoFields(_skin);
    }

    private void LoadIntoFields(SkinDefinition skin)
    {
        NavPaneBackgroundBox.Text = skin.NavPaneBackground;
        FileListBackgroundBox.Text = skin.FileListBackground;
        PanelBackgroundBox.Text = skin.PanelBackground;
        AccentColorBox.Text = skin.AccentColor;
        BackgroundImagePathBox.Text = skin.BackgroundImagePath ?? string.Empty;
        OpacitySlider.Value = skin.BackgroundImageOpacity;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Title = "Choose a background image",
        };
        if (dialog.ShowDialog(this) == true)
            BackgroundImagePathBox.Text = dialog.FileName;
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e) => BackgroundImagePathBox.Text = string.Empty;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _skin.NavPaneBackground = NavPaneBackgroundBox.Text;
        _skin.FileListBackground = FileListBackgroundBox.Text;
        _skin.PanelBackground = PanelBackgroundBox.Text;
        _skin.AccentColor = AccentColorBox.Text;
        _skin.BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePathBox.Text) ? null : BackgroundImagePathBox.Text;
        _skin.BackgroundImageOpacity = OpacitySlider.Value;

        ThemeService.ApplySkin(_skin);
        ThemeService.Save(_skin);
        ThemeService.SaveLastUsedSkinName("default");
    }

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        _skin = SkinDefinition.Default;
        LoadIntoFields(_skin);
        ThemeService.ApplySkin(_skin);
        ThemeService.Save(_skin);
    }
}
