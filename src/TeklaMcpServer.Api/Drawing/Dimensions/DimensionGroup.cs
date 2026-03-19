using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroup
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public DimensionType DomainDimensionType { get; set; }
    public string DimensionType => DomainDimensionType.ToString();
    public DimensionSourceKind SourceKind { get; set; }
    public DimensionGeometryKind GeometryKind { get; set; }
    public string TeklaDimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public int TopDirection { get; set; }
    public (double X, double Y)? Direction { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public double MaximumDistance { get; set; }
    public List<DimensionItem> DimensionList { get; } = [];
    public List<DimensionItem> Members => DimensionList;

    public void SortMembers()
    {
        DimensionList.Sort(static (left, right) =>
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
        Bounds = TeklaDrawingDimensionsApi.CombineBounds(DimensionList.Select(static item => item.Bounds));
        MaximumDistance = DimensionList.Count == 0
            ? 0
            : DimensionList.Max(static item => new[]
            {
                System.Math.Abs(item.Distance),
                item.LeadLineMain?.Length ?? 0,
                item.LeadLineSecond?.Length ?? 0
            }.Max());

        LeadLineMain = DimensionList
            .Select(static item => item.LeadLineMain)
            .Where(static line => line != null)
            .Cast<DrawingLineInfo>()
            .OrderByDescending(static line => line.Length)
            .FirstOrDefault();
        LeadLineSecond = DimensionList
            .Select(static item => item.LeadLineSecond)
            .Where(static line => line != null)
            .Cast<DrawingLineInfo>()
            .OrderByDescending(static line => line.Length)
            .FirstOrDefault();
    }
}
