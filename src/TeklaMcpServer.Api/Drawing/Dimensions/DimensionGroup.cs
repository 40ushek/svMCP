using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionReferenceLine
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }

    public DrawingBoundsInfo Bounds => TeklaDrawingDimensionsApi.CreateBoundsInfo(
        System.Math.Min(StartX, EndX),
        System.Math.Min(StartY, EndY),
        System.Math.Max(StartX, EndX),
        System.Math.Max(StartY, EndY));
}

internal sealed class DimensionGroup
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public (double X, double Y)? Direction { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DimensionReferenceLine? ReferenceLine { get; set; }
    public double MaximumDistance { get; set; }
    public List<DimensionGroupMember> Members { get; } = [];

    public void SortMembers()
    {
        Members.Sort(static (left, right) => left.SortKey.CompareTo(right.SortKey));
    }

    public void RefreshMetrics()
    {
        Bounds = TeklaDrawingDimensionsApi.CombineBounds(Members.Select(static m => m.Bounds));
        MaximumDistance = Members.Count == 0 ? 0 : Members.Max(static m => m.Distance);
    }
}
