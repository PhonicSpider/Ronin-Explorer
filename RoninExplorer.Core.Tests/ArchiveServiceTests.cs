using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class ArchiveServiceTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "RoninExplorerArchiveTests_" + Guid.NewGuid());

    public ArchiveServiceTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void CompressThenExtract_RoundTripsFileContent()
    {
        var sourceDir = Path.Combine(_scratchDir, "source");
        Directory.CreateDirectory(sourceDir);
        var file = Path.Combine(sourceDir, "a.txt");
        File.WriteAllText(file, "hello world");

        var zipPath = ArchiveService.CompressToZip([file], _scratchDir);
        Assert.True(File.Exists(zipPath));
        Assert.EndsWith(".zip", zipPath);

        var extractDir = ArchiveService.ExtractZip(zipPath);
        var extractedFile = Path.Combine(extractDir, "a.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("hello world", File.ReadAllText(extractedFile));
    }

    [Fact]
    public void CompressToZip_IncludesFolderContentsRecursively()
    {
        var folder = Path.Combine(_scratchDir, "myfolder");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "top.txt"), "top");
        File.WriteAllText(Path.Combine(folder, "nested", "inner.txt"), "inner");

        var zipPath = ArchiveService.CompressToZip([folder], _scratchDir);
        var extractDir = ArchiveService.ExtractZip(zipPath);

        Assert.True(File.Exists(Path.Combine(extractDir, "myfolder", "top.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "myfolder", "nested", "inner.txt")));
    }

    [Fact]
    public void ExtractZip_DeduplicatesDestinationFolderName()
    {
        var sourceDir = Path.Combine(_scratchDir, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "content");
        var zipPath = ArchiveService.CompressToZip([sourceDir], _scratchDir);

        var first = ArchiveService.ExtractZip(zipPath);
        var second = ArchiveService.ExtractZip(zipPath);

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }
}
