using LocoTool.Core.Services;

namespace LocoTool.Tests;

public class ParsingServiceTests
{
    [Fact]
    public void CanResolve_WhenNoParsersLoaded_ReturnsNullSafely()
    {
        var svc = new ParsingService();
        var parser = svc.Resolve("non-existent", null, null);
        Assert.Null(parser);
    }
}

