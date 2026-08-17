using System.IO;

namespace RoninExplorer.Core.Engine;

// ── File system helpers ─────────────────────────────────────────────────────
// Small, dependency-free, unit-testable utilities shared by the listing and
// search engines: reparse-point detection (junction/symlink loop guard),
// filename matching, long-path detection, and byte formatting.
//
// Adapted from Ronin_Disk_Manager's Engine/FileSystemHelpers.cs — copied
// near-verbatim since this logic is UI-agnostic and has no Disk-Manager-
// specific fields to strip.
public static class FileSystemHelpers
{
    // Legacy MAX_PATH. The classic shell APIs cannot handle paths at or
    // beyond this without the extended-length "\\?\" prefix.
    public const int LegacyMaxPath = 260;

    /// <summary>
    /// True when the entry is a reparse point (junction, symbolic link, or mount
    /// point). Listing/search must not follow these: doing so can double-count
    /// or spin forever on a self-referential loop.
    /// </summary>
    public static bool IsReparsePoint(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // If attributes can't be read, treat it as a reparse point so we skip it.
            return true;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> matches the search
    /// <paramref name="query"/>. Wildcard queries (containing * or ?) use simple
    /// expression matching; plain queries match as a case-insensitive substring.
    /// </summary>
    public static bool MatchesQuery(string name, string query, bool isWildcard)
    {
        if (string.IsNullOrEmpty(name)) return false;

        if (isWildcard)
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                query, name, ignoreCase: true);

        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Convenience overload that derives the wildcard flag from the query.</summary>
    public static bool MatchesQuery(string name, string query)
        => MatchesQuery(name, query, query.Contains('*') || query.Contains('?'));

    /// <summary>True when a path is long enough to need the extended-length prefix.</summary>
    public static bool ExceedsLegacyMaxPath(string path)
        => !string.IsNullOrEmpty(path) && path.Length >= LegacyMaxPath;

    /// <summary>
    /// Adds the "\\?\" extended-length prefix to a rooted local path when needed,
    /// so enumeration and IO APIs can address paths beyond MAX_PATH. Paths that
    /// already carry a device prefix, or UNC/relative paths, are returned as-is.
    /// </summary>
    public static string ToExtendedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))   return path; // UNC — leave alone
        if (!Path.IsPathRooted(path)) return path;
        return @"\\?\" + path;
    }

    /// <summary>
    /// When true (default) sizes use binary units (1 KB = 1024 bytes); when false
    /// they use decimal units (1 KB = 1000 bytes). Set once at startup from settings.
    /// </summary>
    public static bool UseBinaryUnits { get; set; } = true;

    /// <summary>Human-readable byte size (KB / MB / GB), honoring <see cref="UseBinaryUnits"/>.</summary>
    public static string FormatBytes(long bytes)
    {
        double kb = UseBinaryUnits ? 1024.0 : 1000.0;
        double mb = kb * kb;
        double gb = mb * kb;

        if (bytes >= gb) return $"{bytes / gb:F1} GB";
        if (bytes >= mb) return $"{bytes / mb:F0} MB";
        return $"{bytes / kb:F0} KB";
    }
}
