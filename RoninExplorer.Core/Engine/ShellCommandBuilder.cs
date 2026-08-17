using System.Text;

namespace RoninExplorer.Core.Engine;

// ── PowerShell command builder ──────────────────────────────────────────────
// Pure functions that assemble the PowerShell command strings for the Move,
// Copy, and Delete actions. Copied verbatim from Ronin_Disk_Manager's
// Engine/ShellCommandBuilder.cs. Not wired into interactive ops (those use
// BasicFileOperations/RecycleBin — a per-click PowerShell process-start would
// be too slow for live UI) — this is for a future batch/WhatIf-preview flow
// in the Tools panel, matching Disk Manager's role for it.
public static class ShellCommandBuilder
{
    /// <summary>Options describing a single file operation.</summary>
    public sealed record FileOpOptions
    {
        public string SourcePath  { get; init; } = string.Empty;
        public string Destination { get; init; } = string.Empty;
        public bool   Force       { get; init; }
        public bool   Recurse     { get; init; }
        public bool   WhatIf      { get; init; }
        public bool   Verbose     { get; init; }
        public bool   LiteralPath { get; init; }
        public string Filter      { get; init; } = string.Empty;
        public string Include     { get; init; } = string.Empty;
        public string Exclude     { get; init; } = string.Empty;
    }

    /// <summary>Doubles single quotes so a path is safe inside a single-quoted PS string.</summary>
    public static string EscapePs(string path) => path.Replace("'", "''");

    public static string BuildMove(FileOpOptions o)
    {
        var sb = new StringBuilder();
        AppendPathArg(sb, "Move-Item", o.SourcePath, o.LiteralPath);
        sb.Append($" -Destination '{EscapePs(o.Destination)}'");
        if (o.Force)   sb.Append(" -Force");
        if (o.Verbose) sb.Append(" -Verbose");
        if (o.WhatIf)  sb.Append(" -WhatIf");
        AppendFilters(sb, o);
        return sb.ToString();
    }

    public static string BuildCopy(FileOpOptions o)
    {
        var sb = new StringBuilder();
        AppendPathArg(sb, "Copy-Item", o.SourcePath, o.LiteralPath);
        sb.Append($" -Destination '{EscapePs(o.Destination)}'");
        if (o.Recurse) sb.Append(" -Recurse");   // required to copy a folder's contents
        if (o.Force)   sb.Append(" -Force");
        if (o.Verbose) sb.Append(" -Verbose");
        if (o.WhatIf)  sb.Append(" -WhatIf");
        AppendFilters(sb, o);
        return sb.ToString();
    }

    public static string BuildDelete(FileOpOptions o)
    {
        var sb = new StringBuilder();
        AppendPathArg(sb, "Remove-Item", o.SourcePath, o.LiteralPath);
        if (o.Force)   sb.Append(" -Force");
        if (o.Recurse) sb.Append(" -Recurse");
        if (o.Verbose) sb.Append(" -Verbose");
        if (o.WhatIf)  sb.Append(" -WhatIf");
        AppendFilters(sb, o);
        return sb.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AppendPathArg(StringBuilder sb, string cmdlet, string path, bool literal)
    {
        sb.Append(literal
            ? $"{cmdlet} -LiteralPath '{EscapePs(path)}'"
            : $"{cmdlet} -Path '{EscapePs(path)}'");
    }

    private static void AppendFilters(StringBuilder sb, FileOpOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.Filter))  sb.Append($" -Filter '{o.Filter}'");
        if (!string.IsNullOrWhiteSpace(o.Include)) sb.Append($" -Include {o.Include}");
        if (!string.IsNullOrWhiteSpace(o.Exclude)) sb.Append($" -Exclude {o.Exclude}");
    }
}
