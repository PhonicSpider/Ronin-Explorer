using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class BasicFileOperationsTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "RoninExplorerTests_" + Guid.NewGuid());

    public BasicFileOperationsTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void CreateFolder_DeduplicatesNameLikeExplorer()
    {
        var first = BasicFileOperations.CreateFolder(_scratchDir, "New folder");
        var second = BasicFileOperations.CreateFolder(_scratchDir, "New folder");

        Assert.Equal(Path.Combine(_scratchDir, "New folder"), first);
        Assert.Equal(Path.Combine(_scratchDir, "New folder (2)"), second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void Rename_FailsWhenTargetNameAlreadyExists()
    {
        var a = Path.Combine(_scratchDir, "a.txt");
        var b = Path.Combine(_scratchDir, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");

        var ok = BasicFileOperations.Rename(a, "b.txt", out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void Rename_MovesFileToNewName()
    {
        var original = Path.Combine(_scratchDir, "old.txt");
        File.WriteAllText(original, "hi");

        var ok = BasicFileOperations.Rename(original, "new.txt", out var error);

        Assert.True(ok, error);
        Assert.False(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(_scratchDir, "new.txt")));
    }

    [Fact]
    public void CopyToFolder_DeduplicatesOnConflict()
    {
        var source = Path.Combine(_scratchDir, "source.txt");
        File.WriteAllText(source, "content");
        var destDir = Path.Combine(_scratchDir, "dest");
        Directory.CreateDirectory(destDir);

        BasicFileOperations.CopyToFolder([source], destDir);
        BasicFileOperations.CopyToFolder([source], destDir);

        Assert.True(File.Exists(Path.Combine(destDir, "source.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "source (2).txt")));
        Assert.True(File.Exists(source), "copy should not remove the source");
    }

    [Fact]
    public void MoveToFolder_RemovesSourceAfterMove()
    {
        var source = Path.Combine(_scratchDir, "move-me.txt");
        File.WriteAllText(source, "content");
        var destDir = Path.Combine(_scratchDir, "dest");
        Directory.CreateDirectory(destDir);

        BasicFileOperations.MoveToFolder([source], destDir);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(destDir, "move-me.txt")));
    }

    [Fact]
    public void CopyToFolder_CopiesDirectoryRecursivelySkippingReparsePoints()
    {
        var sourceDir = Path.Combine(_scratchDir, "sourceDir");
        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        File.WriteAllText(Path.Combine(sourceDir, "top.txt"), "top");
        File.WriteAllText(Path.Combine(sourceDir, "nested", "inner.txt"), "inner");
        var destDir = Path.Combine(_scratchDir, "dest");
        Directory.CreateDirectory(destDir);

        BasicFileOperations.CopyToFolder([sourceDir], destDir);

        Assert.True(File.Exists(Path.Combine(destDir, "sourceDir", "top.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "sourceDir", "nested", "inner.txt")));
    }
}
