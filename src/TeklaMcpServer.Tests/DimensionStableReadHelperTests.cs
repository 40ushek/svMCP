using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionStableReadHelperTests
{
    [Fact]
    public void ReadStable_ReturnsFreshValue_WhenSecondReadStabilizes()
    {
        var reads = new Queue<string>(["stale", "fresh", "fresh"]);
        var sleepCalls = new List<int>();

        var result = DimensionStableReadHelper.ReadStable(
            read: () => reads.Dequeue(),
            fingerprint: static value => value,
            sleep: delay =>
            {
                if (delay > 0)
                    sleepCalls.Add(delay);
            });

        Assert.Equal("fresh", result);
        Assert.Equal([50, 150], sleepCalls);
    }

    [Fact]
    public void ReadStable_StopsEarly_WhenSecondReadMatchesFirst()
    {
        var readCount = 0;

        var result = DimensionStableReadHelper.ReadStable(
            read: () =>
            {
                readCount++;
                return "stable";
            },
            fingerprint: static value => value,
            sleep: static _ => { });

        Assert.Equal("stable", result);
        Assert.Equal(2, readCount);
    }

    [Fact]
    public void ReadStable_ReturnsLastValue_WhenSnapshotsKeepChanging()
    {
        var reads = new Queue<string>(["first", "second", "third"]);

        var result = DimensionStableReadHelper.ReadStable(
            read: () => reads.Dequeue(),
            fingerprint: static value => value,
            sleep: static _ => { });

        Assert.Equal("third", result);
    }
}
