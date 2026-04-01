using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BaseViewSelectionTests
{
    [Fact]
    public void Select_ReturnsResolvedWhenSingleEligibleCandidateExists()
    {
        var topView = CreateView(View.ViewTypes.TopView, width: 120, height: 80, originX: 100, originY: 50);
        var views = new List<View>
        {
            ViewTestHelper.Create(View.ViewTypes.SectionView),
            topView
        };

        var result = BaseViewSelection.Select(views);

        Assert.Same(topView, result.View);
        Assert.Equal(BaseViewSelectionKind.Resolved, result.SelectionKind);
        Assert.False(result.IsFallback);
        Assert.Equal("single-base-candidate", result.Reason);
    }

    [Fact]
    public void Select_PrefersFrontViewShortcutWhenFrontViewExists()
    {
        var frontView = CreateView(View.ViewTypes.FrontView, width: 100, height: 100, originX: 50, originY: 50);
        var topView = CreateView(View.ViewTypes.TopView, width: 140, height: 90, originX: 90, originY: 90);
        var views = new List<View>
        {
            topView,
            frontView
        };

        var result = BaseViewSelection.Select(views);

        Assert.Same(frontView, result.View);
        Assert.Equal(BaseViewSelectionKind.Fallback, result.SelectionKind);
        Assert.True(result.IsFallback);
        Assert.Equal("front-view-shortcut", result.Reason);
    }

    [Fact]
    public void Select_ReturnsRankedFallbackWhenNoFrontViewExists()
    {
        var largerCentralTop = CreateView(View.ViewTypes.TopView, width: 200, height: 120, originX: 100, originY: 100);
        var smallerOffsetModel = CreateView(View.ViewTypes.ModelView, width: 80, height: 60, originX: 220, originY: 60);
        var views = new List<View>
        {
            smallerOffsetModel,
            largerCentralTop
        };

        var result = BaseViewSelection.Select(views);

        Assert.Same(largerCentralTop, result.View);
        Assert.Equal(BaseViewSelectionKind.Fallback, result.SelectionKind);
        Assert.True(result.IsFallback);
        Assert.Equal("ranked-base-candidate", result.Reason);
    }

    [Fact]
    public void Select_ReturnsUnresolvedWhenNoEligibleBaseCandidateExists()
    {
        var views = new List<View>
        {
            ViewTestHelper.Create(View.ViewTypes.SectionView),
            CreateView(View.ViewTypes.DetailView)
        };

        var result = BaseViewSelection.Select(views);

        Assert.Null(result.View);
        Assert.Equal(BaseViewSelectionKind.Unresolved, result.SelectionKind);
        Assert.False(result.IsFallback);
        Assert.Equal("no-base-candidate", result.Reason);
    }

    private static View CreateView(
        View.ViewTypes viewType,
        double width = 0,
        double height = 0,
        double originX = 0,
        double originY = 0)
        => ViewTestHelper.Create(viewType, width: width, height: height, originX: originX, originY: originY);
}
