using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>A unit direction in the drawing view plane, independent of line placement.</summary>
internal sealed class DimensionDirection
{
    public double X { get; }
    public double Y { get; }

    public DimensionDirection(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
            throw new ArgumentException("Dimension direction must be finite.");
        var scale = Math.Max(Math.Abs(x), Math.Abs(y));
        if (scale == 0) throw new ArgumentException("Dimension direction must be nonzero.");
        var sx = x / scale;
        var sy = y / scale;
        var length = Math.Sqrt(sx * sx + sy * sy);
        X = sx / length;
        Y = sy / length;
    }
}
