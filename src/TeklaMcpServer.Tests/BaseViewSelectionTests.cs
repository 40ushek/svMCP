using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BaseViewSelectionTests
{
    [Fact]
    public void Select_ReturnsFrontViewShortcutWhenFrontViewExists()
    {
        var frontView = new FrontView();
        var views = new List<View>
        {
            new TopView(),
            frontView
        };

        var result = BaseViewSelection.Select(views);

        Assert.Same(frontView, result.View);
        Assert.Equal(BaseViewSelectionKind.Fallback, result.SelectionKind);
        Assert.True(result.IsFallback);
        Assert.Equal("front-view-shortcut", result.Reason);
    }

    [Fact]
    public void Select_PrefersFrontViewWhenOtherViewsAlsoExist()
    {
        var frontView = new FrontView();
        var sectionView = new SectionView();
        var views = new List<View>
        {
            new TopView(),
            sectionView,
            frontView
        };

        var result = BaseViewSelection.Select(views);

        Assert.Same(frontView, result.View);
        Assert.NotSame(sectionView, result.View);
        Assert.Equal(BaseViewSelectionKind.Fallback, result.SelectionKind);
        Assert.Equal("front-view-shortcut", result.Reason);
    }

    [Fact]
    public void Select_ReturnsUnresolvedWhenFrontViewIsMissing()
    {
        var views = new List<View>
        {
            new TopView(),
            new SectionView()
        };

        var result = BaseViewSelection.Select(views);

        Assert.Null(result.View);
        Assert.Equal(BaseViewSelectionKind.Unresolved, result.SelectionKind);
        Assert.False(result.IsFallback);
        Assert.Equal("no-base-view", result.Reason);
    }
}
