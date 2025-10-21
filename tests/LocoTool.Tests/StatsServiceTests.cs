using LocoTool.Core.Services;

namespace LocoTool.Tests;

public class StatsServiceTests
{
    [Fact]
    public void EstimateCost_ComputesBatchesAndCosts()
    {
        var stats = new StatsService(new TableIo());
        var (b, exact, padded, paddedChars) = stats.EstimateCost(25_000, 10_000, 250.0);
        Assert.Equal(3, b);
        Assert.True(exact > 0);
        Assert.True(padded > exact);
        Assert.Equal(30_000, paddedChars);
    }
}

