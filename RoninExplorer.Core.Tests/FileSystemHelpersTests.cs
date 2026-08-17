using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class FileSystemHelpersTests
{
    [Theory]
    [InlineData("readme.txt", "read", false, true)]
    [InlineData("readme.txt", "READ", false, true)]
    [InlineData("readme.txt", "xyz", false, false)]
    [InlineData("readme.txt", "*.txt", true, true)]
    [InlineData("readme.txt", "*.md", true, false)]
    public void MatchesQuery_MatchesWildcardAndSubstring(string name, string query, bool isWildcard, bool expected)
        => Assert.Equal(expected, FileSystemHelpers.MatchesQuery(name, query, isWildcard));

    [Fact]
    public void ExceedsLegacyMaxPath_TrueAtOrBeyondThreshold()
    {
        var justUnder = new string('a', FileSystemHelpers.LegacyMaxPath - 1);
        var atLimit = new string('a', FileSystemHelpers.LegacyMaxPath);

        Assert.False(FileSystemHelpers.ExceedsLegacyMaxPath(justUnder));
        Assert.True(FileSystemHelpers.ExceedsLegacyMaxPath(atLimit));
    }

    [Fact]
    public void ToExtendedPath_PrefixesRootedLocalPathOnly()
    {
        Assert.Equal(@"\\?\C:\Foo\Bar", FileSystemHelpers.ToExtendedPath(@"C:\Foo\Bar"));
        Assert.Equal(@"\\server\share\file", FileSystemHelpers.ToExtendedPath(@"\\server\share\file"));
        Assert.Equal(@"\\?\C:\Foo", FileSystemHelpers.ToExtendedPath(@"\\?\C:\Foo"));
        Assert.Equal("relative\\path", FileSystemHelpers.ToExtendedPath("relative\\path"));
    }

    [Theory]
    [InlineData(5000, "5 KB")]
    [InlineData(2 * 1024 * 1024, "2 MB")]
    public void FormatBytes_UsesBinaryUnitsByDefault(long bytes, string expected)
        => Assert.Equal(expected, FileSystemHelpers.FormatBytes(bytes));
}
