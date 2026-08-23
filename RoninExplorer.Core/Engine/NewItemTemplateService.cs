using Microsoft.Win32;
using RoninExplorer.Core.Engine.Native;

namespace RoninExplorer.Core.Engine;

/// <summary>
/// One entry in the "New" menu — either a built-in extension (Text Document,
/// Bitmap Image, ...) or a third-party one registered by an installed app
/// (e.g. Google Docs/Sheets/Slides via Google Drive). <see cref="TemplateFilePath"/>
/// and <see cref="Data"/> are mutually exclusive; when both are null the item
/// is created as an empty file.
/// </summary>
public sealed record NewItemTemplate(string DisplayName, string Extension, string? TemplateFilePath, byte[]? Data);

// ── "New" menu registry discovery ───────────────────────────────────────────
// Windows Explorer's "New" submenu isn't hardcoded — it's built by scanning
// HKEY_CLASSES_ROOT for file extensions that carry a ShellNew subkey. This
// mirrors that exact mechanism so Ronin Explorer's New menu matches whatever
// is actually registered on the machine, including third-party registrations
// (Office, Google Drive's .gdoc/.gsheet/.gslides shortcuts, etc.) with no
// hardcoded list to maintain.
public static class NewItemTemplateService
{
    public static Task<List<NewItemTemplate>> EnumerateTemplatesAsync(CancellationToken ct = default)
        => Task.Run(EnumerateTemplates, ct);

    private static List<NewItemTemplate> EnumerateTemplates()
    {
        var results = new List<NewItemTemplate>();
        var shellNewDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ShellNew");
        var classesRoot = Registry.ClassesRoot;

        string[] extensionKeyNames;
        try { extensionKeyNames = classesRoot.GetSubKeyNames(); }
        catch { return results; }

        foreach (var name in extensionKeyNames)
        {
            if (name.Length < 2 || name[0] != '.') continue;

            try
            {
                using var extKey = classesRoot.OpenSubKey(name);
                using var shellNewKey = extKey?.OpenSubKey("ShellNew");
                if (extKey is null || shellNewKey is null) continue;

                string? templatePath = null;
                byte[]? data = null;

                if (shellNewKey.GetValue("FileName") is string fileName && !string.IsNullOrWhiteSpace(fileName))
                {
                    templatePath = Path.IsPathRooted(fileName)
                        ? fileName
                        : Path.Combine(shellNewDir, fileName);
                    if (!File.Exists(templatePath)) continue; // registered but missing — don't offer a broken entry
                }
                else if (shellNewKey.GetValue("Data") is byte[] bytes)
                {
                    data = bytes;
                }
                else if (!shellNewKey.GetValueNames().Contains("NullFile", StringComparer.OrdinalIgnoreCase))
                {
                    // Command-based ShellNew (runs an arbitrary command to build the file) or
                    // an unrecognized mechanism — skip rather than guess at side effects.
                    continue;
                }

                var displayName = ResolveDisplayName(classesRoot, extKey, shellNewKey, name);
                results.Add(new NewItemTemplate(displayName, name, templatePath, data));
            }
            catch
            {
                // One malformed/inaccessible registry entry shouldn't break the whole menu.
            }
        }

        return [.. results.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    private static string ResolveDisplayName(RegistryKey classesRoot, RegistryKey extKey, RegistryKey shellNewKey, string extension)
    {
        if (shellNewKey.GetValue("ItemName") is string itemName)
        {
            var resolved = ResolveIndirectString(itemName);
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        }

        // Fall back to the friendly name of the extension's associated ProgID
        // (the same lookup Explorer itself uses for the "Type" column).
        if (extKey.GetValue(null) is string progId && !string.IsNullOrWhiteSpace(progId))
        {
            try
            {
                using var progIdKey = classesRoot.OpenSubKey(progId);
                if (progIdKey?.GetValue(null) is string friendlyName && !string.IsNullOrWhiteSpace(friendlyName))
                    return friendlyName;
            }
            catch { /* fall through to raw extension */ }
        }

        return extension.TrimStart('.').ToUpperInvariant() + " File";
    }

    /// <summary>
    /// Resolves an indirect string reference in the form "@[path],-resourceId"
    /// (MUI-style, used throughout the shell for localized names) via
    /// LoadLibraryEx + LoadString. Returns the value as-is if it isn't in that form.
    /// </summary>
    private static string? ResolveIndirectString(string value)
    {
        if (!value.StartsWith('@')) return value;

        var stripped = value[1..];
        var commaIndex = stripped.LastIndexOf(',');
        if (commaIndex < 0) return null;

        var dllPath = Environment.ExpandEnvironmentVariables(stripped[..commaIndex]);
        if (!int.TryParse(stripped[(commaIndex + 1)..], out var resourceId)) return null;

        var handle = NativeMethods.LoadLibraryEx(dllPath, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var buffer = new System.Text.StringBuilder(512);
            int len = NativeMethods.LoadString(handle, (uint)Math.Abs(resourceId), buffer, buffer.Capacity);
            return len > 0 ? buffer.ToString() : null;
        }
        finally
        {
            NativeMethods.FreeLibrary(handle);
        }
    }

    /// <summary>Creates a new item from <paramref name="template"/> under <paramref name="parentPath"/>, Explorer-style ("New {DisplayName}{Extension}", deduplicated). Returns the created file's full path.</summary>
    public static string CreateFromTemplate(NewItemTemplate template, string parentPath)
    {
        var desiredName = $"New {template.DisplayName}{template.Extension}";

        if (template.TemplateFilePath is not null)
            return BasicFileOperations.CreateFileFromTemplate(parentPath, desiredName, template.TemplateFilePath);
        if (template.Data is not null)
            return BasicFileOperations.CreateFileWithContent(parentPath, desiredName, template.Data);
        return BasicFileOperations.CreateFile(parentPath, desiredName);
    }
}
