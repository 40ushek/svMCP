using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// LengthList must report what the sheet prints: the span PROJECTED onto the dimension axis.
///
/// It used to be the straight-line distance from the first point of the array, which matched only
/// when the points happened to be collinear. Comparing an exported PDF against the old output
/// showed 2555.25 printed as 2546, 456.91 as 448 and 298.53 as 301 — every difference caused by a
/// point sitting off the axis. Those invented fractions were then read as snap drift, and the
/// differences between them produced negative segments, which a dimension cannot have.
///
/// RealLengthList keeps the straight-line distance on purpose: it is the honest point-to-point
/// value. DimensionOperations matches packets against LengthList with a tolerance, so the two must
/// stay distinct.
/// </summary>
public class DimensionItemLengthProjectionTests
{
    private static DimensionItem BuildItem(double directionX, double directionY, params (double X, double Y)[] points)
    {
        var item = new DimensionItem
        {
            DimensionId = 1,
            DirectionX = directionX,
            DirectionY = directionY
        };

        var list = new List<DrawingPointInfo>();
        for (var i = 0; i < points.Length; i++)
            list.Add(new DrawingPointInfo { X = points[i].X, Y = points[i].Y, Order = i });

        item.ReplacePointList(list);
        return item;
    }

    [Fact]
    public void CollinearPoints_ProjectionEqualsStraightDistance()
    {
        var item = BuildItem(1, 0, (0, 0), (600, 0), (1225, 0));

        Assert.Equal(new[] { 600d, 1225d }, item.LengthList);
        Assert.Equal(item.LengthList, item.RealLengthList);
    }

    [Fact]
    public void PointOffTheAxis_IsProjected_NotMeasuredAsHypotenuse()
    {
        // The real case from the sheet: a chain running vertically with one point inset by 223 mm.
        // Printed value is the vertical span, 2545.5 — not the 2555.25 hypotenuse.
        var item = BuildItem(0, 1, (0, 2135.8), (223, -409.7));

        Assert.Equal(2545.5, item.LengthList[0], 1);
        Assert.Equal(2555.25, item.RealLengthList[0], 1);
    }

    [Fact]
    public void InclinedAxis_ProjectsOntoItsOwnDirection()
    {
        // 3-4-5 triangle. Along (0.6, 0.8) the point (30, 40) sits 50 away; (-40, 30) is
        // perpendicular to the axis, so it shares the origin's position along it.
        var item = BuildItem(0.6, 0.8, (0, 0), (30, 40), (-40, 30));

        // Two points project to 0 and one to 50, measured from the near end of the span.
        Assert.Equal(new[] { 0d, 50d }, item.LengthList);

        // In space both are 50 away from the first point.
        Assert.Equal(50d, item.RealLengthList[0], 6);
        Assert.Equal(50d, item.RealLengthList[1], 6);
    }

    [Fact]
    public void PointsOrderedAgainstTheAxis_StayPositive()
    {
        // Descending X on a horizontal chain. Values are measured from the near end of the span,
        // so they rise regardless of the order the points arrive in — the old formula produced
        // negatives here once differences between them were taken.
        var item = BuildItem(1, 0, (1443, 0), (1338, 0), (163, 0), (0, 0));

        Assert.All(item.LengthList, length => Assert.True(length >= 0, $"negative length {length}"));
        Assert.Equal(new[] { 163d, 1338d, 1443d }, item.LengthList);

        // Straight-line distances still run from the first point of the array.
        Assert.Equal(new[] { 105d, 1280d, 1443d }, item.RealLengthList);

        var steps = new List<double>();
        for (var i = 1; i < item.LengthList.Count; i++)
            steps.Add(item.LengthList[i] - item.LengthList[i - 1]);

        Assert.All(steps, step => Assert.True(step >= 0, $"negative step {step}"));
    }

    [Fact]
    public void WithoutADirection_FallsBackToStraightDistance()
    {
        // Some items are built before a direction is known; they must keep working rather than
        // silently report zeros.
        var item = BuildItem(0, 0, (0, 0), (300, 400));

        Assert.Equal(500d, item.LengthList[0], 6);
        Assert.Equal(500d, item.RealLengthList[0], 6);
    }

    [Fact]
    public void ProjectionIsIndependentOfHowFarThePointsSitFromTheAxis()
    {
        // Two chains measuring the same span, one reading off inset parts. The printed numbers are
        // identical; only RealLengthList differs. This is what made "snap drift" look like a defect.
        var onAxis = BuildItem(0, 1, (0, 0), (0, 1000));
        var inset = BuildItem(0, 1, (0, 0), (150, 1000));

        Assert.Equal(onAxis.LengthList[0], inset.LengthList[0], 6);
        Assert.NotEqual(Math.Round(onAxis.RealLengthList[0], 2), Math.Round(inset.RealLengthList[0], 2));
    }
}
