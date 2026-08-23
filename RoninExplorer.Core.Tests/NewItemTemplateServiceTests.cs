using System.IO.Compression;
using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class NewItemTemplateServiceTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "RoninExplorerTests_" + Guid.NewGuid());

    public NewItemTemplateServiceTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task EnumerateTemplatesAsync_AlwaysIncludesExplorerCoreFixtures()
    {
        // .txt/.bmp/.zip have no ShellNew registry backing on a stock Windows
        // install (confirmed by direct registry inspection), so these three
        // rely entirely on the hardcoded baseline fallback - if that fallback
        // ever regresses, this is the test that catches it.
        var templates = await NewItemTemplateService.EnumerateTemplatesAsync();

        Assert.Contains(templates, t => t.Extension == ".txt");
        Assert.Contains(templates, t => t.Extension == ".bmp");
        Assert.Contains(templates, t => t.Extension == ".zip");
    }

    [Fact]
    public async Task CreateFromTemplateAsync_TextDocument_CreatesEmptyDeduplicatedFile()
    {
        var template = new NewItemTemplate("Text Document", ".txt", null, []);

        var first = await NewItemTemplateService.CreateFromTemplateAsync(template, _scratchDir);
        var second = await NewItemTemplateService.CreateFromTemplateAsync(template, _scratchDir);

        Assert.Equal(Path.Combine(_scratchDir, "New Text Document.txt"), first);
        Assert.Equal(Path.Combine(_scratchDir, "New Text Document (2).txt"), second);
        Assert.Equal(0, new FileInfo(first).Length);
    }

    [Fact]
    public async Task CreateFromTemplateAsync_BitmapImage_WritesAValidOpenableBmpHeader()
    {
        var bmpBytes = BuildMinimalBitmapViaBaseline();
        var template = new NewItemTemplate("Bitmap image", ".bmp", null, bmpBytes);

        var created = await NewItemTemplateService.CreateFromTemplateAsync(template, _scratchDir);
        var bytes = await File.ReadAllBytesAsync(created);

        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 2)); // file size field matches actual length
    }

    [Fact]
    public async Task CreateFromTemplateAsync_CompressedFolder_WritesAValidEmptyZip()
    {
        var zipBytes = BuildMinimalZipViaBaseline();
        var template = new NewItemTemplate("Compressed (zipped) Folder", ".zip", null, zipBytes);

        var created = await NewItemTemplateService.CreateFromTemplateAsync(template, _scratchDir);

        using var archive = ZipFile.OpenRead(created);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public async Task CreateFromTemplateAsync_DataAsPlainString_WritesTextContentNotSkipped()
    {
        // Mirrors .rtf's real ShellNew registration, where "Data" is a
        // REG_SZ ("{\rtf1}"), not REG_BINARY - the exact bug that silently
        // dropped Rich Text Document from the menu before this fix.
        var template = new NewItemTemplate("Rich Text Document", ".rtf", null, System.Text.Encoding.UTF8.GetBytes(@"{\rtf1}"));

        var created = await NewItemTemplateService.CreateFromTemplateAsync(template, _scratchDir);
        var content = await File.ReadAllTextAsync(created);

        Assert.Equal(@"{\rtf1}", content);
    }

    // Baseline byte layouts are private to the service - these mirror them so the test
    // doesn't need reflection, while still asserting on the actual production behavior
    // via CreateFromTemplateAsync above.
    private static byte[] BuildMinimalBitmapViaBaseline()
    {
        const int width = 1, height = 1, bitsPerPixel = 24;
        int rowSize = ((width * bitsPerPixel + 31) / 32) * 4;
        int pixelDataSize = rowSize * height;
        int fileSize = 14 + 40 + pixelDataSize;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);
        w.Write(14 + 40);
        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)bitsPerPixel);
        w.Write(0);
        w.Write(pixelDataSize);
        w.Write(0); w.Write(0);
        w.Write(0); w.Write(0);
        w.Write((byte)0xFF); w.Write((byte)0xFF); w.Write((byte)0xFF); w.Write((byte)0x00);
        return ms.ToArray();
    }

    private static byte[] BuildMinimalZipViaBaseline() =>
        [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
}
