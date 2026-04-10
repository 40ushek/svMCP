using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutContextBuilderTests
{
    [Fact]
    public void ResolveExcludeViewIds_AutoExcludesAllViews_WhenCallerDidNotProvideList()
    {
        var views = new DrawingViewsResult
        {
            Views =
            [
                CreateView(10),
                CreateView(20),
                CreateView(30)
            ]
        };

        var result = DrawingLayoutContextBuilder.ResolveExcludeViewIds(views, null);

        Assert.Equal([10, 20, 30], result.OrderBy(static id => id).ToArray());
    }

    [Fact]
    public void ResolveExcludeViewIds_PreservesExplicitExcludeList_WhenCallerProvidedOne()
    {
        var views = new DrawingViewsResult
        {
            Views =
            [
                CreateView(10),
                CreateView(20),
                CreateView(30)
            ]
        };
        IReadOnlyCollection<int> explicitExclude = [20];

        var result = DrawingLayoutContextBuilder.ResolveExcludeViewIds(views, explicitExclude);

        Assert.Same(explicitExclude, result);
    }

    private static DrawingViewInfo CreateView(int id)
    {
        return new DrawingViewInfo
        {
            Id = id,
            ViewType = "FrontView",
            SemanticKind = "BaseProjected",
            Name = $"view-{id}",
            Scale = 20,
            Width = 100,
            Height = 50
        };
    }
}
