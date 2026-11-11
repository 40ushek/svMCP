using TeklaMcpServer.Api.Drawing.ViewLayout;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ConstraintViewSideResolverTests
{
    // Base view of a horizontal member: looks along -Y, model X to the right, model Z up.
    private static readonly (double, double, double) BaseX = (1, 0, 0);
    private static readonly (double, double, double) BaseY = (0, 0, 1);

    [Fact]
    public void Projection_StandsOnTheObserversSide()
    {
        // Seen from above: the view's Z axis points up, along the base view's Y.
        var fromAbove = Resolve(viewX: (1, 0, 0), viewY: (0, 1, 0), standsWhereItLooks: false);
        // Seen from below.
        var fromBelow = Resolve(viewX: (1, 0, 0), viewY: (0, -1, 0), standsWhereItLooks: false);
        // Seen from the +X end.
        var fromTheRight = Resolve(viewX: (0, 1, 0), viewY: (0, 0, 1), standsWhereItLooks: false);

        Assert.Equal(SectionPlacementSide.Top, fromAbove);
        Assert.Equal(SectionPlacementSide.Bottom, fromBelow);
        Assert.Equal(SectionPlacementSide.Right, fromTheRight);
    }

    [Fact]
    public void SectionOrEndView_StandsWhereItsArrowsPoint_AwayFromTheObserver()
    {
        // Observer at the +X end looks toward -X: the arrows point left.
        var lookingLeft = Resolve(viewX: (0, 1, 0), viewY: (0, 0, 1), standsWhereItLooks: true);
        var lookingRight = Resolve(viewX: (0, -1, 0), viewY: (0, 0, 1), standsWhereItLooks: true);
        // Observer above looks down: the arrows point down.
        var lookingDown = Resolve(viewX: (1, 0, 0), viewY: (0, 1, 0), standsWhereItLooks: true);
        var lookingUp = Resolve(viewX: (1, 0, 0), viewY: (0, -1, 0), standsWhereItLooks: true);

        Assert.Equal(SectionPlacementSide.Left, lookingLeft);
        Assert.Equal(SectionPlacementSide.Right, lookingRight);
        Assert.Equal(SectionPlacementSide.Bottom, lookingDown);
        Assert.Equal(SectionPlacementSide.Top, lookingUp);
    }

    [Fact]
    public void TurnedBaseView_AViewSeenFromAbove_StandsBesideIt()
    {
        // A vertical member drawn lying down: the base view's X axis is model Z.
        var side = ConstraintViewSideResolver.Resolve(
            baseAxisX: (0, 0, 1),
            baseAxisY: (-1, 0, 0),
            viewAxisX: (1, 0, 0),
            viewAxisY: (0, 1, 0),
            standsWhereItLooks: false);

        Assert.Equal(SectionPlacementSide.Right, side);
    }

    [Fact]
    public void ViewLookingTheSameWayAsTheBaseView_OrAtAnAngle_HasNoSide()
    {
        var sameWay = Resolve(viewX: (1, 0, 0), viewY: (0, 0, 1), standsWhereItLooks: false);
        var backView = Resolve(viewX: (-1, 0, 0), viewY: (0, 0, 1), standsWhereItLooks: false);
        var skewed = Resolve(viewX: (0.7071, 0.7071, 0), viewY: (0, 0, 1), standsWhereItLooks: true);
        var degenerate = Resolve(viewX: (1, 0, 0), viewY: (2, 0, 0), standsWhereItLooks: true);

        Assert.Equal(SectionPlacementSide.Unknown, sameWay);
        Assert.Equal(SectionPlacementSide.Unknown, backView);
        Assert.Equal(SectionPlacementSide.Unknown, skewed);
        Assert.Equal(SectionPlacementSide.Unknown, degenerate);
    }

    private static SectionPlacementSide Resolve(
        (double, double, double) viewX,
        (double, double, double) viewY,
        bool standsWhereItLooks)
        => ConstraintViewSideResolver.Resolve(BaseX, BaseY, viewX, viewY, standsWhereItLooks);
}
