using System.IO;
using System.Text.Json;

namespace RoninExplorer.App.Services;

/// <summary>Persists the Favorites/pinned-folders list as a plain JSON array of paths under %AppData%\Ronin_Explorer, matching the storage pattern already used for skins/keybinds.</summary>
public static class PinnedFoldersService
{
    private static readonly string PinnedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ronin_Explorer", "pinned.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(PinnedPath)) return [];
            var json = File.ReadAllText(PinnedPath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return []; // corrupt/unreadable file — start empty rather than crash startup
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PinnedPath)!);
        File.WriteAllText(PinnedPath, JsonSerializer.Serialize(paths.ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }
}
