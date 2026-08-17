using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class ToolsEngineTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "RoninExplorerToolsTests_" + Guid.NewGuid());

    public ToolsEngineTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task DuplicateFinder_FindsByteForByteDuplicatesOnly()
    {
        File.WriteAllText(Path.Combine(_scratchDir, "a.txt"), "same content");
        File.WriteAllText(Path.Combine(_scratchDir, "b.txt"), "same content");
        File.WriteAllText(Path.Combine(_scratchDir, "c.txt"), "different content!!");

        var files = await FolderListingService.ListFilesRecursiveAsync(_scratchDir);
        var groups = await DuplicateFinder.FindDuplicatesAsync(files);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(["a.txt", "b.txt"], group.Files.Select(f => f.Name).OrderBy(n => n));
    }

    [Fact]
    public void CleanupScanner_ShouldClean_RespectsPatternAndAge()
    {
        var cutoff = DateTime.Now;
        var old = cutoff.AddDays(-10);
        var recent = cutoff.AddDays(1);

        Assert.True(CleanupScanner.ShouldClean("app.log", "*.log", old, cutoff));
        Assert.False(CleanupScanner.ShouldClean("app.log", "*.log", recent, cutoff), "too recent to clean");
        Assert.False(CleanupScanner.ShouldClean("app.txt", "*.log", old, cutoff), "pattern doesn't match");
    }

    [Fact]
    public async Task CleanupScanner_FindAsync_OnlyReturnsFilesOlderThanCutoff()
    {
        var oldFile = Path.Combine(_scratchDir, "old.log");
        var newFile = Path.Combine(_scratchDir, "new.log");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));
        File.SetLastWriteTime(newFile, DateTime.Now);

        var results = await CleanupScanner.FindAsync(_scratchDir, "*.log", olderThanDays: 7, recurse: true);

        var hit = Assert.Single(results);
        Assert.Equal("old.log", hit.Name);
    }

    [Fact]
    public async Task FolderListingService_GetFolderSizeAsync_SumsNestedFiles()
    {
        Directory.CreateDirectory(Path.Combine(_scratchDir, "nested"));
        File.WriteAllBytes(Path.Combine(_scratchDir, "top.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_scratchDir, "nested", "inner.bin"), new byte[50]);

        var size = await FolderListingService.GetFolderSizeAsync(_scratchDir);

        Assert.Equal(150, size);
    }
}
