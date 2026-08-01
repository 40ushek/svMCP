using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionObservationContractTests
{
    [Fact]
    public void ResultHasStableHeaderDefaults()
    {
        var result = new DimensionObservationResult();

        Assert.Equal(1, result.Header.SchemaVersion);
        Assert.Equal("mm", result.Header.Units);
        Assert.Equal("json-utf8", result.Header.PartsPayloadEncoding);
        Assert.NotNull(result.ViewContext);
        Assert.NotNull(result.DimensionContext);
    }

    [Fact]
    public void ResultKeepsTheTwoExistingContextPayloadsSeparate()
    {
        var view = new GetDrawingViewContextResult { ViewId = 12, ViewType = "FrontView" };
        var dimensions = new GetDimensionContextsResult { ViewId = 12, Total = 1 };
        var result = new DimensionObservationResult
        {
            ViewContext = view,
            DimensionContext = dimensions
        };

        Assert.Same(view, result.ViewContext);
        Assert.Same(dimensions, result.DimensionContext);
        Assert.Equal(12, result.ViewContext.ViewId);
        Assert.Equal(1, result.DimensionContext.Total);
    }
}
