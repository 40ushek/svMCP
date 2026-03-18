using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroup
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public int TopDirection { get; set; }
    public (double X, double Y)? Direction { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public double MaximumDistance { get; set; }
    public List<DimensionGroupMember> Members { get; } = [];

    public void SortMembers()
    {
        Members.Sort(static (left, right) =>
        {
            if (left.LeadLineMain != null && right.LeadLineMain != null)
            {
                var byX = left.LeadLineMain.StartX.CompareTo(right.LeadLineMain.StartX);
                if (byX != 0)
                    return byX;

                var byY = left.LeadLineMain.StartY.CompareTo(right.LeadLineMain.StartY);
                if (byY != 0)
                    return byY;
            }

            return left.SortKey.CompareTo(right.SortKey);
        });
    }

    public void RefreshMetrics()
    {
        Bounds = TeklaDrawingDimensionsApi.CombineBounds(Members.Select(static m => m.Bounds));
        MaximumDistance = Members.Count == 0
            ? 0
            : Members.Max(static m => new[]
            {
                System.Math.Abs(m.Distance),
                m.LeadLineMain?.Length ?? 0,
                m.LeadLineSecond?.Length ?? 0
            }.Max());
    }
}
