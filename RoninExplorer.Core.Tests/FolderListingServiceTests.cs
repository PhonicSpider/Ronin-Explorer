using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class FolderListingServiceTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "RoninExplorerListingTests_" + Guid.NewGuid());

    public FolderListingServiceTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ListFolderAsync_ExcludesHiddenAndSystemFilesByDefault()
    {
        var visible = Path.Combine(_scratchDir, "visible.txt");
        var hidden = Path.Combine(_scratchDir, "hidden.txt");
        File.WriteAllText(visible, "v");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var results = await FolderListingService.ListFolderAsync(_scratchDir, includeHidden: false);

        Assert.Contains(results, r => r.Name == "visible.txt");
        Assert.DoesNotContain(results, r => r.Name == "hidden.txt");
    }

    [Fact]
    public async Task ListFolderAsync_IncludesHiddenFilesWhenRequested()
    {
        var hidden = Path.Combine(_scratchDir, "hidden.txt");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var results = await FolderListingService.ListFolderAsync(_scratchDir, includeHidden: true);

        Assert.Contains(results, r => r.Name == "hidden.txt");
    }
}
