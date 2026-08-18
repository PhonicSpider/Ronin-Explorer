using System.IO;
using System.IO.Compression;

namespace RoninExplorer.Core.Engine;

// ── Archive service ──────────────────────────────────────────────────────
// Extract/compress ZIP archives — Explorer's "Extract All..." and "Compress
// to ZIP file" context menu actions. Built on System.IO.Compression (no
// extra dependency); handles multi-select compression (files and folders
// together at the top level), which ZipFile.CreateFromDirectory alone
// doesn't support since it only zips one whole directory.
public static class ArchiveService
{
    /// <summary>
    /// Extracts <paramref name="zipPath"/> into a sibling folder named after
    /// the archive (Explorer's own default target), deduplicating like
    /// BasicFileOperations does if that folder already exists. Returns the
    /// extraction folder's path.
    /// </summary>
    public static string ExtractZip(string zipPath)
    {
        var parent = Path.GetDirectoryName(zipPath) ?? throw new InvalidOperationException("Zip file has no parent folder.");
        var baseName = Path.GetFileNameWithoutExtension(zipPath);

        var destDir = Path.Combine(parent, baseName);
        int suffix = 2;
        while (Directory.Exists(destDir))
        {
            destDir = Path.Combine(parent, $"{baseName} ({suffix})");
            suffix++;
        }

        ZipFile.ExtractToDirectory(zipPath, destDir);
        return destDir;
    }

    /// <summary>
    /// Compresses the given files/folders into a new ZIP in <paramref name="destFolder"/>,
    /// named after the first selected item (matching Explorer's naming) with
    /// dedup on conflict. Returns the created archive's path.
    /// </summary>
    public static string CompressToZip(IReadOnlyList<string> sourcePaths, string destFolder)
    {
        if (sourcePaths.Count == 0) throw new ArgumentException("Nothing selected to compress.", nameof(sourcePaths));

        var baseName = Path.GetFileNameWithoutExtension(sourcePaths[0].TrimEnd(Path.DirectorySeparatorChar));
        var zipPath = Path.Combine(destFolder, baseName + ".zip");
        int suffix = 2;
        while (File.Exists(zipPath))
        {
            zipPath = Path.Combine(destFolder, $"{baseName} ({suffix}).zip");
            suffix++;
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var source in sourcePaths)
        {
            if (Directory.Exists(source))
                AddDirectoryToArchive(archive, source, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
            else if (File.Exists(source))
                archive.CreateEntryFromFile(source, Path.GetFileName(source));
        }

        return zipPath;
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDir, string entryPrefix)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var entryName = $"{entryPrefix}/{relative}".Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryName);
        }
    }
}
