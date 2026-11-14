using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionMoveProtocolTests
{
    [Fact]
    public void FailedModifyDoesNotCommitOrClaimMovement()
    {
        var result = DimensionMoveProtocol.Execute(42, 80, 20, _ => false,
            () => throw new InvalidOperationException("unexpected commit"),
            () => throw new InvalidOperationException("unexpected read"));
        Assert.False(result.Moved);
        Assert.Equal(80, result.NewDistance);
        Assert.Contains("Modify", result.Reason);
    }

    [Theory]
    [InlineData(100d, true)]
    [InlineData(90d, false)]
    [InlineData(null, false)]
    public void SuccessDependsOnFreshDistance(double? actual, bool moved)
    {
        var committed = false;
        var result = DimensionMoveProtocol.Execute(42, 80, 20, requested => {
            Assert.Equal(100, requested);
            return true;
        }, () => committed = true, () => { Assert.True(committed); return actual; });
        Assert.Equal(moved, result.Moved);
        Assert.Equal(actual ?? 80, result.NewDistance);
    }
}
