using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoninExplorer.App.Models;

namespace RoninExplorer.App.Services;

/// <summary>
/// Loads/saves skins as JSON under %AppData%\Ronin_Explorer\skins and applies
/// one at runtime by swapping a freshly-built ResourceDictionary into
/// Application.Current.Resources — every themeable value in the UI is bound
/// via DynamicResource, so the swap re-renders instantly with no restart.
/// </summary>
public static class ThemeService
{
    private static readonly string SkinsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ronin_Explorer", "skins");

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ronin_Explorer", "settings.json");

    private static ResourceDictionary? _appliedSkinDictionary;

    public static void ApplySkin(SkinDefinition skin)
    {
        var accentColor = (Color)ColorConverter.ConvertFromString(SafeHex(skin.AccentColor))!;

        var dict = new ResourceDictionary
        {
            ["NavPaneBackgroundBrush"] = MakeBrush(skin.NavPaneBackground),
            ["PanelBackgroundBrush"] = MakeBrush(skin.PanelBackground),
            ["AccentBrush"] = MakeBrush(skin.AccentColor),
            ["TextPrimaryBrush"] = MakeBrush(skin.TextPrimary),
            // Subtle, theme-aware file-row selection/hover tints — a plain
            // Foreground Setter keeps list text readable regardless (see
            // MainWindow.xaml's ListViewItem template), but the highlight
            // color itself needs to be visibly less saturated than the raw
            // accent color to read as "selected," not "alarm," matching
            // Explorer's own quiet selection tint.
            ["SelectionHighlightBrush"] = MakeTintedBrush(accentColor, 0.28),
            ["HoverHighlightBrush"] = MakeTintedBrush(accentColor, 0.12),
        };

        // A background image, when set, overrides the flat file-list color —
        // this is the customization stock Explorer has no equivalent for.
        dict["FileListBackgroundBrush"] =
            !string.IsNullOrWhiteSpace(skin.BackgroundImagePath) && File.Exists(skin.BackgroundImagePath)
                ? MakeImageBrush(skin.BackgroundImagePath, skin.BackgroundImageOpacity)
                : MakeBrush(skin.FileListBackground);

        var app = Application.Current;
        if (_appliedSkinDictionary is not null)
            app.Resources.MergedDictionaries.Remove(_appliedSkinDictionary);

        app.Resources.MergedDictionaries.Add(dict);
        _appliedSkinDictionary = dict;
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    private static string SafeHex(string hex)
    {
        try
        {
            ColorConverter.ConvertFromString(hex);
            return hex;
        }
        catch
        {
            return SkinDefinition.Default.AccentColor;
        }
    }

    private static SolidColorBrush MakeTintedBrush(Color baseColor, double opacity)
    {
        var brush = new SolidColorBrush(baseColor) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    private static ImageBrush MakeImageBrush(string path, double opacity)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();

        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    // ── Persistence ───────────────────────────────────────────────────────

    public static SkinDefinition LoadOrDefault(string name = "default")
    {
        try
        {
            var path = Path.Combine(SkinsDir, name + ".json");
            if (!File.Exists(path)) return SkinDefinition.Default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SkinDefinition>(json) ?? SkinDefinition.Default;
        }
        catch
        {
            return SkinDefinition.Default; // corrupt/unreadable skin file — fall back rather than crash startup
        }
    }

    public static void Save(SkinDefinition skin, string name = "default")
    {
        Directory.CreateDirectory(SkinsDir);
        var path = Path.Combine(SkinsDir, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(skin, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<string> ListSkins()
    {
        if (!Directory.Exists(SkinsDir)) return [];
        return [.. Directory.EnumerateFiles(SkinsDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f))];
    }

    /// <summary>Which skin name to load on startup — separate from the skin files themselves so switching skins doesn't require renaming files.</summary>
    public static string LoadLastUsedSkinName()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return "default";
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("lastSkin", out var v) ? v.GetString() ?? "default" : "default";
        }
        catch
        {
            return "default";
        }
    }

    public static void SaveLastUsedSkinName(string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { lastSkin = name }));
    }
}
