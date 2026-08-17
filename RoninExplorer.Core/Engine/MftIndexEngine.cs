using System.IO;
using System.Runtime.InteropServices;
using RoninExplorer.Core.Engine.Native;
using RoninExplorer.Core.Models;

namespace RoninExplorer.Core.Engine;

// ── MFT index engine ─────────────────────────────────────────────────────────
// Reads the NTFS USN journal via FSCTL_ENUM_USN_DATA on a raw volume handle —
// the WizTree technique — for near-instant whole-volume enumeration. Adapted
// from Ronin_Disk_Manager's Engine/MftScanEngine.cs, but reshaped for a
// different job: that engine builds a DiskNode TREE scoped to one root, with
// an eager per-directory size pass, for one-shot disk-usage analysis. This
// engine instead builds a FLAT whole-volume list with no size/tree work at
// all, because its only job is powering instant name search — the index
// needs to become queryable the moment enumeration finishes, and size/date
// aren't part of what search matches on.
//
// Requires administrator privileges (the volume handle needs GENERIC_READ on
// the raw device path \\.\C:) — VolumeIndexManager only invokes this when
// running elevated; see ElevationService.
internal sealed class MftIndexEngine
{
    private const int ReadBufferSize = 524_288; // 512 KB — fewer round trips to the kernel

    public Task<List<VolumeIndexEntry>> BuildAsync(string driveRoot, CancellationToken ct = default)
        => Task.Run(() => Build(driveRoot, ct), ct);

    private static List<VolumeIndexEntry> Build(string driveRoot, CancellationToken ct)
    {
        var devicePath = $@"\\.\{driveRoot.TrimEnd('\\')}";

        using var volumeHandle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (volumeHandle.IsInvalid)
            throw new InvalidOperationException($"Cannot open volume {devicePath}. Requires administrator privileges.");

        var entries = EnumerateMftEntries(volumeHandle, ct);
        ct.ThrowIfCancellationRequested();

        var pathCache = BuildPathCache(entries, driveRoot, ct);
        ct.ThrowIfCancellationRequested();

        var result = new List<VolumeIndexEntry>(entries.Count);
        foreach (var kv in entries)
        {
            if (string.IsNullOrEmpty(kv.Value.Name)) continue; // volume root's own record has no name
            if (!pathCache.TryGetValue(kv.Key, out var path)) continue;

            result.Add(new VolumeIndexEntry
            {
                Name = kv.Value.Name,
                FullPath = path,
                IsDirectory = kv.Value.IsDirectory,
            });
        }

        return result;
    }

    // ── USN journal enumeration (copied from MftScanEngine.cs) ───────────────
    private static Dictionary<ulong, MftEntry> EnumerateMftEntries(
        Microsoft.Win32.SafeHandles.SafeFileHandle volumeHandle,
        CancellationToken ct)
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
