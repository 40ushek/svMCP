using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutPlacement
{
    public int Id { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double AnchorX { get; set; }

    public double AnchorY { get; set; }

    public bool HasLeaderLine { get; set; }

    public bool HasAxis { get; set; }

    public double AxisDx { get; set; }

    public double AxisDy { get; set; }

    public bool CanMove { get; set; }

    public List<double[]> LocalCorners { get; set; } = new();

    public MarkLayoutPlacement Clone()
    {
        return new MarkLayoutPlacement
        {
            Id = Id,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            AnchorX = AnchorX,
            AnchorY = AnchorY,
            HasLeaderLine = HasLeaderLine,
            HasAxis = HasAxis,
            AxisDx = AxisDx,
            AxisDy = AxisDy,
            CanMove = CanMove,
            LocalCorners = LocalCorners.Select(c => new[] { c[0], c[1] }).ToList()
        };
    }
}
