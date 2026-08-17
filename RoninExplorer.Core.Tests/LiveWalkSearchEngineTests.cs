using RoninExplorer.Core.Engine;

namespace RoninExplorer.Core.Tests;

public class LiveWalkSearchEngineTests
{
    [Fact]
    public async Task SearchAsync_ReturnsEmptyForBlankQuery()
    {
        var results = await LiveWalkSearchEngine.SearchAsync("");
        Assert.Empty(results);
    }
}

public class VolumeIndexManagerTests
{
    [Fact]
    public void Search_ReturnsEmptyBeforeAnyBuild()
    {
        var manager = new VolumeIndexManager();
        Assert.False(manager.HasAnyIndex);
        Assert.Empty(manager.Search("anything"));
    }

    [Fact]
    public void Search_ReturnsEmptyForBlankQuery()
    {
        var manager = new VolumeIndexManager();
        Assert.Empty(manager.Search(""));
    }
}
