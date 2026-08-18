using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RoninExplorer.Core.Engine.Native;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

/// <summary>Result of a full MftIndexEngine.BuildAsync — the initial index plus the USN-journal cursor VolumeIndexManager needs to start reading deltas from.</summary>
public sealed class IndexBuildResult
{
    public required Dictionary<ulong, VolumeIndexEntry> EntriesByFrn { get; init; }
    public required ulong JournalId { get; init; }
    public required long StartUsn { get; init; }
}

/// <summary>One changed MFT record from ReadDeltaAsync — a create/rename/update (IsDelete = false) or a deletion (IsDelete = true, only Frn is meaningful).</summary>
public sealed class UsnChange
{
    public required ulong Frn { get; init; }
    public required ulong ParentFrn { get; init; }
    public required string Name { get; init; }
    public required bool IsDirectory { get; init; }
    public required bool IsDelete { get; init; }
}

public sealed class DeltaResult
{
    public required List<UsnChange> Changes { get; init; }
    public required long NextUsn { get; init; }
}

// ── MFT index engine ─────────────────────────────────────────────────────────
// Reads the NTFS USN journal via FSCTL_ENUM_USN_DATA on a raw volume handle —
// the WizTree technique — for near-instant whole-volume enumeration. Adapted
// from Ronin_Disk_Manager's Engine/MftScanEngine.cs, but reshaped for a
// different job: that engine builds a DiskNode TREE scoped to one root, with
// an eager per-directory size pass, for one-shot disk-usage analysis. This
// engine instead builds a FLAT whole-volume index with no size/tree work at
// all, because its only job is powering instant name search — the index
// needs to become queryable the moment enumeration finishes, and size/date
// aren't part of what search matches on.
//
// ReadDeltaAsync is the piece with no precedent in Disk Manager: it reads
// only the USN journal records since a given cursor (FSCTL_READ_USN_JOURNAL)
// instead of re-walking the whole volume, letting VolumeIndexManager keep its
// index live without a full rebuild.
//
// Requires administrator privileges (the volume handle needs GENERIC_READ on
// the raw device path \\.\C:) — VolumeIndexManager only invokes this when
// running elevated; see ElevationService.
internal sealed class MftIndexEngine
{
    private const int ReadBufferSize = 524_288; // 512 KB — fewer round trips to the kernel

    public Task<IndexBuildResult> BuildAsync(string driveRoot, CancellationToken ct = default)
        => Task.Run(() => Build(driveRoot, ct), ct);

    public Task<DeltaResult> ReadDeltaAsync(string driveRoot, ulong journalId, long sinceUsn, CancellationToken ct = default)
        => Task.Run(() => ReadDelta(driveRoot, journalId, sinceUsn, ct), ct);

    private static IndexBuildResult Build(string driveRoot, CancellationToken ct)
    {
        using var volumeHandle = OpenVolume(driveRoot);

        // Query the journal BEFORE enumerating, so StartUsn is a safe lower
        // bound — any change that lands mid-enumeration gets picked up again
        // by the very first delta read (applying it twice is harmless; a
        // delta upsert of an already-current entry is a no-op in effect).
        var journal = QueryJournal(volumeHandle);

        var entries = EnumerateMftEntries(volumeHandle, ct);
        ct.ThrowIfCancellationRequested();

        var pathCache = BuildPathCache(entries, driveRoot, ct);
        ct.ThrowIfCancellationRequested();

        var byFrn = new Dictionary<ulong, VolumeIndexEntry>(entries.Count);
        foreach (var kv in entries)
        {
            if (string.IsNullOrEmpty(kv.Value.Name)) continue; // volume root's own record has no name
            if (!pathCache.TryGetValue(kv.Key, out var path)) continue;

            byFrn[kv.Key] = new VolumeIndexEntry
            {
                Name = kv.Value.Name,
                FullPath = path,
                IsDirectory = kv.Value.IsDirectory,
                Frn = kv.Key,
                ParentFrn = kv.Value.ParentFrn,
            };
        }

        return new IndexBuildResult
        {
            EntriesByFrn = byFrn,
            JournalId = journal.UsnJournalID,
            StartUsn = journal.NextUsn,
        };
    }

    // ── Incremental delta read ────────────────────────────────────────────────
    private static DeltaResult ReadDelta(string driveRoot, ulong journalId, long sinceUsn, CancellationToken ct)
    {
        using var volumeHandle = OpenVolume(driveRoot);

        var changes = new List<UsnChange>();
        var readData = new NativeMethods.READ_USN_JOURNAL_DATA_V0
        {
            StartUsn = sinceUsn,
            ReasonMask = 0xFFFFFFFF, // every reason — deletes matter as much as creates/renames
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0, // don't block waiting for new records; just return what's available now
            UsnJournalID = journalId,
        };

        var buffer = Marshal.AllocHGlobal(ReadBufferSize);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool ok = NativeMethods.DeviceIoControlReadJournal(
                    volumeHandle,
                    NativeMethods.FSCTL_READ_USN_JOURNAL,
                    ref readData,
                    Marshal.SizeOf<NativeMethods.READ_USN_JOURNAL_DATA_V0>(),
                    buffer,
                    ReadBufferSize,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!ok)
                    throw new InvalidOperationException($"FSCTL_READ_USN_JOURNAL failed (Win32 error {Marshal.GetLastWin32Error()}).");

                // First 8 bytes are always the next-call cursor (a USN, i.e. Int64).
                var nextUsn = Marshal.ReadInt64(buffer);
                readData.StartUsn = nextUsn;

                if (bytesReturned <= 8) break; // caught up — no records beyond the cursor

                int offset = 8;
                while (offset < (int)bytesReturned)
                {
                    var record = Marshal.PtrToStructure<NativeMethods.USN_RECORD_V2>(buffer + offset);
                    if (record.RecordLength == 0) break;

                    if (record.MajorVersion == 2 && record.FileNameLength > 0)
                    {
                        var name = Marshal.PtrToStringUni(buffer + offset + record.FileNameOffset, record.FileNameLength / 2);
                        if (!string.IsNullOrEmpty(name))
                        {
                            changes.Add(new UsnChange
                            {
                                Frn = NormalizeFrn(record.FileReferenceNumber),
                                ParentFrn = NormalizeFrn(record.ParentFileReferenceNumber),
                                Name = name,
                                IsDirectory = (record.FileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0,
                                IsDelete = (record.Reason & NativeMethods.USN_REASON_FILE_DELETE) != 0,
                            });
                        }
                    }

                    offset += (int)record.RecordLength;
                }

                if (bytesReturned < ReadBufferSize - 1024) break; // last page was partial — nothing more queued right now
            }

            return new DeltaResult { Changes = changes, NextUsn = readData.StartUsn };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle OpenVolume(string driveRoot)
    {
        var devicePath = $@"\\.\{driveRoot.TrimEnd('\\')}";
        var handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new InvalidOperationException($"Cannot open volume {devicePath}. Requires administrator privileges.");

        return handle;
    }

    private static NativeMethods.USN_JOURNAL_DATA_V0 QueryJournal(SafeFileHandle volumeHandle)
    {
        bool ok = NativeMethods.DeviceIoControlQueryJournal(
            volumeHandle,
            NativeMethods.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            out var journalData,
            Marshal.SizeOf<NativeMethods.USN_JOURNAL_DATA_V0>(),
            out _,
            IntPtr.Zero);

        if (!ok)
            throw new InvalidOperationException($"FSCTL_QUERY_USN_JOURNAL failed (Win32 error {Marshal.GetLastWin32Error()}). The volume may not have an active USN journal.");

        return journalData;
    }

    // ── USN journal enumeration (copied from MftScanEngine.cs) ───────────────
    private static Dictionary<ulong, MftEntry> EnumerateMftEntries(SafeFileHandle volumeHandle, CancellationToken ct)
    {
        var entries = new Dictionary<ulong, MftEntry>(500_000);

        var enumData = new NativeMethods.MFT_ENUM_DATA_V0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue
        };

        var buffer = Marshal.AllocHGlobal(ReadBufferSize);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool ok = NativeMethods.DeviceIoControl(
                    volumeHandle,
                    NativeMethods.FSCTL_ENUM_USN_DATA,
                    ref enumData,
                    Marshal.SizeOf<NativeMethods.MFT_ENUM_DATA_V0>(),
                    buffer,
                    ReadBufferSize,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == NativeMethods.ERROR_HANDLE_EOF) break;
                    throw new InvalidOperationException($"FSCTL_ENUM_USN_DATA failed (Win32 error {err}).");
                }

                enumData.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(buffer);

                int offset = 8;
                while (offset < (int)bytesReturned)
                {
                    var record = Marshal.PtrToStructure<NativeMethods.USN_RECORD_V2>(buffer + offset);
                    if (record.RecordLength == 0) break;

                    if (record.MajorVersion == 2 && record.FileNameLength > 0)
                    {
                        var name = Marshal.PtrToStringUni(buffer + offset + record.FileNameOffset, record.FileNameLength / 2);
                        if (!string.IsNullOrEmpty(name))
                        {
                            var frn = NormalizeFrn(record.FileReferenceNumber);
                            var pFrn = NormalizeFrn(record.ParentFileReferenceNumber);

                            entries[frn] = new MftEntry
                            {
                                Frn = frn,
                                ParentFrn = pFrn,
                                Name = name,
                                IsDirectory = (record.FileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0
                            };
                        }
                    }

                    offset += (int)record.RecordLength;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    // ── Full path resolution (copied from MftScanEngine.cs) ──────────────────
    private static Dictionary<ulong, string> BuildPathCache(Dictionary<ulong, MftEntry> entries, string volumeRoot, CancellationToken ct)
    {
        var cache = new Dictionary<ulong, string>(entries.Count);
        foreach (var frn in entries.Keys)
        {
            ct.ThrowIfCancellationRequested();
            if (!cache.ContainsKey(frn))
                ResolvePath(frn, entries, cache, volumeRoot);
        }
        return cache;
    }

    private static void ResolvePath(ulong startFrn, Dictionary<ulong, MftEntry> entries, Dictionary<ulong, string> cache, string volumeRoot)
    {
        var chain = new List<(ulong Frn, string Name)>();
        var current = startFrn;

        while (true)
        {
            if (cache.ContainsKey(current)) break;

            if (!entries.TryGetValue(current, out var entry) || entry.ParentFrn == current)
            {
                cache[current] = volumeRoot;
                break;
            }

            chain.Add((current, entry.Name));
            current = entry.ParentFrn;
        }

        var basePath = cache[current];
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            basePath = Path.Combine(basePath, chain[i].Name);
            cache[chain[i].Frn] = basePath;
        }
    }

    private static ulong NormalizeFrn(ulong frn) => frn & 0x0000_FFFF_FFFF_FFFF;

    private sealed record MftEntry
    {
        public ulong Frn { get; init; }
        public ulong ParentFrn { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsDirectory { get; init; }
    }
}
