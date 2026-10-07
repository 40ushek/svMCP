using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Null disables an axis. Settings are immutable for one calculation.</summary>
internal sealed class OverallDimensionSettings
{
    public DimensionChainSide? HorizontalSide { get; }
    public DimensionChainSide? VerticalSide { get; }
    public double PositionTolerance { get; }
    public bool UseExtentBounds { get; }

    public OverallDimensionSettings(DimensionChainSide? horizontalSide = DimensionChainSide.Bottom,
        DimensionChainSide? verticalSide = DimensionChainSide.Right, double positionTolerance = 0.5,
        bool useExtentBounds = false)
    {
        if (horizontalSide.HasValue && horizontalSide != DimensionChainSide.Bottom && horizontalSide != DimensionChainSide.Top)
            throw new ArgumentException("Horizontal overall dimensions require Top or Bottom.", nameof(horizontalSide));
        if (verticalSide.HasValue && verticalSide != DimensionChainSide.Left && verticalSide != DimensionChainSide.Right)
            throw new ArgumentException("Vertical overall dimensions require Left or Right.", nameof(verticalSide));
        if (double.IsNaN(positionTolerance) || double.IsInfinity(positionTolerance) || positionTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(positionTolerance));
        HorizontalSide = horizontalSide;
        VerticalSide = verticalSide;
        PositionTolerance = positionTolerance;
        UseExtentBounds = useExtentBounds;
    }
}
