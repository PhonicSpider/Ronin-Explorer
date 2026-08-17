using System.IO;
using System.Text.Json;

namespace RoninExplorer.App.Services;

/// <summary>
/// Action-id → gesture-string keybind map (e.g. "Rename" → "F2"). Gesture
/// strings use WPF's own KeyGestureConverter format ("Ctrl+C", "Ctrl+Shift+N")
/// so they parse/format bidirectionally with no custom format to maintain.
/// Storage is a sparse JSON override file — a fresh install matches these
/// defaults without keybinds.json needing to exist at all; only entries that
/// differ from default get written.
/// </summary>
public static class KeybindService
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["Rename"] = "F2",
        ["Delete"] = "Delete",
        ["Copy"] = "Ctrl+C",
        ["Cut"] = "Ctrl+X",
        ["Paste"] = "Ctrl+V",
        ["NewFolder"] = "Ctrl+Shift+N",
        ["Refresh"] = "F5",
    };

    private static readonly string KeybindsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ronin_Explorer", "keybinds.json");

    public static Dictionary<string, string> LoadEffectiveMap()
    {
        var map = new Dictionary<string, string>(Defaults);
        try
        {
            if (File.Exists(KeybindsPath))
            {
                var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(KeybindsPath));
                if (overrides is not null)
                    foreach (var (actionId, gesture) in overrides)
                        map[actionId] = gesture;
            }
        }
        catch
        {
            // Corrupt/unreadable overrides file — fall back to defaults rather than crash startup.
        }
        return map;
    }

    /// <summary>Persists only the entries that differ from <see cref="Defaults"/>.</summary>
    public static void SaveEffectiveMap(IReadOnlyDictionary<string, string> effectiveMap)
    {
        var overrides = effectiveMap
            .Where(kv => !Defaults.TryGetValue(kv.Key, out var def) || !string.Equals(def, kv.Value, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        Directory.CreateDirectory(Path.GetDirectoryName(KeybindsPath)!);
        File.WriteAllText(KeybindsPath, JsonSerializer.Serialize(overrides, new JsonSerializerOptions { WriteIndented = true }));
    }
}
