using CustomsClearance.Platform.Logging;
using Xunit;

namespace CustomsClearance.Platform.Logging.Tests;

public class CorrelationIdAccessorTests
{
    [Fact]
    public void Current_DefaultsToNull()
    {
        Assert.Null(CorrelationIdAccessor.Current);
    }

    [Fact]
    public void Current_CanBeSetAndRead()
    {
        var id = Guid.NewGuid().ToString();

        CorrelationIdAccessor.Current = id;

        Assert.Equal(id, CorrelationIdAccessor.Current);
    }

    [Fact]
    public async Task Current_FlowsThroughAsyncContinuations()
    {
        var id = Guid.NewGuid().ToString();
        CorrelationIdAccessor.Current = id;

        await Task.Delay(1);
        await Task.Yield();

        Assert.Equal(id, CorrelationIdAccessor.Current);
    }
}
