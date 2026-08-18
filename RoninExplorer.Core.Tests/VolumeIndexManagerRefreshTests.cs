using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

// The FSCTL_QUERY_USN_JOURNAL/FSCTL_READ_USN_JOURNAL delta-read path itself
// needs an elevated process against a real NTFS volume handle, which this
// test environment doesn't have (not running as Administrator, and this
// sandbox can't interactively elevate) — so unlike FileOperationServiceTests,
// the native delta-read call could not be exercised end-to-end here. What IS
// tested: RefreshAllAsync is safe to call with nothing indexed yet (the
// no-op path every non-elevated run takes, since BuildAllAsync never starts
// the refresh timer without at least one successfully indexed drive).
public class VolumeIndexManagerRefreshTests
{
    [Fact]
    public async Task RefreshAllAsync_NoOpsSafelyWithNoIndexedDrives()
    {
        var manager = new VolumeIndexManager();
        await manager.RefreshAllAsync();
        Assert.False(manager.HasAnyIndex);
    }
}
