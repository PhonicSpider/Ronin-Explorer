using System.Diagnostics;
using Microsoft.Win32;
using RoninExplorer.Core.Engine.Native;

namespace RoninExplorer.Core.Engine;

/// <summary>
/// One entry in the "New" menu — either a built-in extension (Text Document,
/// Bitmap Image, ...) or a third-party one registered by an installed app
/// (e.g. Google Docs/Sheets/Slides via Google Drive). Exactly one of
/// <see cref="TemplateFilePath"/>, <see cref="Data"/>, or <see cref="Command"/>
/// is set; all null means an empty file.
/// </summary>
public sealed record NewItemTemplate(string DisplayName, string Extension, string? TemplateFilePath, byte[]? Data, string? Command = null);

// ── "New" menu registry discovery ───────────────────────────────────────────
// Windows Explorer's "New" submenu isn't hardcoded — it's built by scanning
// HKEY_CLASSES_ROOT for file extensions that carry a ShellNew subkey. This
// mirrors that exact mechanism so Ronin Explorer's New menu matches whatever
// is actually registered on the machine, including third-party registrations
// (Google Drive's .gdoc/.gsheet/.gslides shortcuts, WinZip, etc.) with no
// hardcoded list to maintain. A handful of entries real Explorer always shows
// (Text Document, Bitmap Image, Compressed Folder) turned out to have NO
// registry backing at all on a live Windows 11 machine — confirmed by
// enumerating every HKCR key with a ShellNew child (only ~10 exist on a
// typical install: .contact, .gdoc/.gsheet/.gslides, .library-ms, .lnk,
// .mdb, .rtf, .wzcloud, Folder). Those three are Explorer-internal fixtures,
// not registry-driven, so they're seeded as a baseline below rather than
// discovered — but only when the registry doesn't already supply that
// extension, so a machine that DOES register them isn't given a duplicate.
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
        catch { extensionKeyNames = []; }

        foreach (var name in extensionKeyNames)
        {
            if (name.Length < 2 || name[0] != '.') continue;

            try
            {
                using var extKey = classesRoot.OpenSubKey(name);
                using var shellNewKey = extKey?.OpenSubKey("ShellNew");
                if (extKey is null || shellNewKey is null) continue;

                var template = BuildTemplate(classesRoot, extKey, shellNewKey, name, shellNewDir);
                if (template is not null) results.Add(template);
            }
            catch
            {
                // One malformed/inaccessible registry entry shouldn't break the whole menu.
            }
        }

        foreach (var baseline in BaselineTemplates())
        {
            if (!results.Any(t => string.Equals(t.Extension, baseline.Extension, StringComparison.OrdinalIgnoreCase)))
                results.Add(baseline);
        }

        return [.. results.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Resolves one ShellNew key into a template, in the same priority order
    /// Explorer itself documents: FileName (copy a template file) &gt; Data
    /// (literal content, string or binary) &gt; Command (run a registered
    /// creator, only when it takes a target path via "%1" — a fixed command
    /// like Access's "msaccess.exe /NEWDB 1" has nowhere for us to point it)
    /// &gt; nothing recognized, which — including a ShellNew key with zero
    /// values at all — defaults to an empty file.
    /// </summary>
    private static NewItemTemplate? BuildTemplate(RegistryKey classesRoot, RegistryKey extKey, RegistryKey shellNewKey, string extension, string shellNewDir)
    {
        var displayName = ResolveDisplayName(classesRoot, extKey, shellNewKey, extension);

        if (shellNewKey.GetValue("FileName") is string fileName && !string.IsNullOrWhiteSpace(fileName))
        {
            var templatePath = Path.IsPathRooted(fileName)
                ? Environment.ExpandEnvironmentVariables(fileName)
                : Path.Combine(shellNewDir, Environment.ExpandEnvironmentVariables(fileName));
            if (File.Exists(templatePath))
                return new NewItemTemplate(displayName, extension, templatePath, null);
        }

        var dataValue = shellNewKey.GetValue("Data");
        if (dataValue is byte[] binaryData)
            return new NewItemTemplate(displayName, extension, null, binaryData);
        if (dataValue is string textData)
            return new NewItemTemplate(displayName, extension, null, System.Text.Encoding.UTF8.GetBytes(textData));

        if (shellNewKey.GetValue("Command") is string command && command.Contains("%1"))
            return new NewItemTemplate(displayName, extension, null, null, command);

        if (shellNewKey.GetValue("FileName") is null && shellNewKey.GetValue("Command") is null)
            return new NewItemTemplate(displayName, extension, null, []);

        return null; // FileName pointed at a missing file, or Command had nowhere to target
    }

    private static string ResolveDisplayName(RegistryKey classesRoot, RegistryKey extKey, RegistryKey shellNewKey, string extension)
    {
        var indirect = shellNewKey.GetValue("ItemName") as string ?? shellNewKey.GetValue("MenuText") as string;
        if (indirect is not null)
        {
            var resolved = ResolveIndirectString(indirect);
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

    /// <summary>
    /// Explorer-internal fixtures with no ShellNew registry backing (verified
    /// against a live Windows 11 install — see the type's remarks). Data
    /// content matches what a real, minimal, openable file of that type needs.
    /// </summary>
    private static IEnumerable<NewItemTemplate> BaselineTemplates()
    {
        yield return new NewItemTemplate("Text Document", ".txt", null, []);
        yield return new NewItemTemplate("Bitmap image", ".bmp", null, BuildMinimalBitmap());
        // Minimal valid empty ZIP: just an End Of Central Directory record (0 entries).
        yield return new NewItemTemplate("Compressed (zipped) Folder", ".zip", null,
            [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    }

    private static byte[] BuildMinimalBitmap()
    {
        const int width = 1, height = 1, bitsPerPixel = 24;
        int rowSize = ((width * bitsPerPixel + 31) / 32) * 4;
        int pixelDataSize = rowSize * height;
        int fileSize = 14 + 40 + pixelDataSize;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);
        w.Write(14 + 40);

        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)bitsPerPixel);
        w.Write(0);
        w.Write(pixelDataSize);
        w.Write(0); w.Write(0);
        w.Write(0); w.Write(0);

        w.Write((byte)0xFF); w.Write((byte)0xFF); w.Write((byte)0xFF); w.Write((byte)0x00);

        return ms.ToArray();
    }

    /// <summary>Creates a new item from <paramref name="template"/> under <paramref name="parentPath"/>, Explorer-style ("New {DisplayName}{Extension}", deduplicated). Returns the created file's full path.</summary>
    public static async Task<string> CreateFromTemplateAsync(NewItemTemplate template, string parentPath)
    {
        var desiredName = $"New {template.DisplayName}{template.Extension}";

        if (template.TemplateFilePath is not null)
            return BasicFileOperations.CreateFileFromTemplate(parentPath, desiredName, template.TemplateFilePath);
        if (template.Data is not null)
            return BasicFileOperations.CreateFileWithContent(parentPath, desiredName, template.Data);
        if (template.Command is not null)
            return await CreateFromCommandAsync(template.Command, parentPath, desiredName);
        return BasicFileOperations.CreateFile(parentPath, desiredName);
    }

    /// <summary>
    /// Runs a registered ShellNew "Command" (e.g. Google Drive File Stream's
    /// .gdoc creator) with "%1" substituted for the deduplicated target path,
    /// via cmd.exe /c so the value's own "quoted exe" + args shape parses
    /// correctly without hand-splitting it. Some of these commands create the
    /// file asynchronously through a background service rather than
    /// synchronously in the launched process, so this briefly polls for it.
    /// </summary>
    private static async Task<string> CreateFromCommandAsync(string command, string parentPath, string desiredName)
    {
        var targetPath = BasicFileOperations.ResolveConflictName(parentPath, desiredName);
        var commandLine = command.Replace("%1", targetPath);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {commandLine}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = parentPath,
        };

        try
        {
            using var process = Process.Start(psi);
        }
        catch
        {
            return targetPath; // couldn't launch — caller's folder refresh just won't find it yet
        }

        for (var i = 0; i < 20 && !File.Exists(targetPath); i++)
            await Task.Delay(250);

        return targetPath;
    }
}
