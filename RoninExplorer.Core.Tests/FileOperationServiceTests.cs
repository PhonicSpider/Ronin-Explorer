using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

// IFileOperation is a classic STA-affinity COM object — xunit's default test
// thread is MTA, so every test here runs its assertions on a dedicated STA
// thread via RunOnSta. This is deliberate, not incidental: it's the same
// validation the M7 plan called for before wiring FileOperationService into
// the interactive UI, given hand-declared COM vtables fail silently/oddly
// rather than with a clear compile error when a method signature is wrong.
public class FileOperationServiceTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "RoninExplorerFileOpTests_" + Guid.NewGuid());

    public FileOperationServiceTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static void RunOnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null) throw captured;
    }

    [Fact]
    public void CopyItems_CreatesCopyAtDestination()
    {
        var source = Path.Combine(_scratchDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destDir = Path.Combine(_scratchDir, "dest");
        Directory.CreateDirectory(destDir);

        FileOperationService.OperationResult result = null!;
        RunOnSta(() => result = FileOperationService.CopyItems([source], destDir));

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(source), "copy should not remove the source");
        Assert.True(File.Exists(Path.Combine(destDir, "source.txt")));
    }

    [Fact]
    public void MoveItems_RelocatesFileAndRemovesSource()
    {
        var source = Path.Combine(_scratchDir, "move-me.txt");
        File.WriteAllText(source, "content");
        var destDir = Path.Combine(_scratchDir, "dest");
        Directory.CreateDirectory(destDir);

        FileOperationService.OperationResult result = null!;
        RunOnSta(() => result = FileOperationService.MoveItems([source], destDir));

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(destDir, "move-me.txt")));
    }

    [Fact]
    public void RenameItem_ChangesFileName()
    {
        var original = Path.Combine(_scratchDir, "old-name.txt");
        File.WriteAllText(original, "content");

        FileOperationService.OperationResult result = null!;
        RunOnSta(() => result = FileOperationService.RenameItem(original, "new-name.txt"));

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(_scratchDir, "new-name.txt")));
    }

    [Fact]
    public void DeleteItems_RemovesFileFromOriginalLocation()
    {
        var target = Path.Combine(_scratchDir, "delete-me.txt");
        File.WriteAllText(target, "content");

        FileOperationService.OperationResult result = null!;
        RunOnSta(() => result = FileOperationService.DeleteItems([target]));

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(target), "file should be gone from its original location (recycled, not left in place)");
    }

    [Fact]
    public void CopyItems_CopiesAFolderRecursively()
    {
        var sourceDir = Path.Combine(_scratchDir, "folder-to-copy");
        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        File.WriteAllText(Path.Combine(sourceDir, "top.txt"), "top");
        File.WriteAllText(Path.Combine(sourceDir, "nested", "inner.txt"), "inner");
        var destDir = Path.Combine(_scratchDir, "dest");
        Directory.CreateDirectory(destDir);

        FileOperationService.OperationResult result = null!;
        RunOnSta(() => result = FileOperationService.CopyItems([sourceDir], destDir));

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(Path.Combine(destDir, "folder-to-copy", "top.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "folder-to-copy", "nested", "inner.txt")));
    }
}
