using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class GetDrawingViewContextResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double ViewScale { get; set; }
    public DrawingBoundsInfo? PartsBounds { get; set; }
    public List<PartInView> Parts { get; set; } = new();
    public List<BoltGroupGeometry> Bolts { get; set; } = new();
    public List<string> GridIds { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}

internal static class DrawingViewContextMapper
{
    public static GetDrawingViewContextResult ToResult(DrawingViewContext context)
    {
        // Keep the transport payload isolated from mutable internal collections.
        // Parts and bolts are already DTO-shaped, so this deep copy is mostly a
        // defensive contract boundary and can be relaxed later if it becomes a
        // measurable cost in single-call bridge usage.
        return new GetDrawingViewContextResult
        {
            Success = true,
            ViewId = context.ViewId ?? 0,
            ViewType = context.ViewType,
            ViewScale = context.ViewScale,
            PartsBounds = context.PartsBounds == null
                ? null
                : new DrawingBoundsInfo
                {
                    MinX = context.PartsBounds.MinX,
                    MinY = context.PartsBounds.MinY,
                    MaxX = context.PartsBounds.MaxX,
                    MaxY = context.PartsBounds.MaxY
                },
            Parts = context.Parts
                .Select(ClonePart)
                .ToList(),
            Bolts = context.Bolts
                .Select(CloneBolt)
                .ToList(),
            GridIds = context.GridIds.ToList(),
            Warnings = context.Warnings.ToList()
        };
    }

    private static PartInView ClonePart(PartInView part)
    {
        return new PartInView
        {
            Success = part.Success,
            ViewId = part.ViewId,
            ModelId = part.ModelId,
            Error = part.Error,
            StartPoint = part.StartPoint.ToArray(),
            EndPoint = part.EndPoint.ToArray(),
            CoordinateSystemOrigin = part.CoordinateSystemOrigin.ToArray(),
            AxisX = part.AxisX.ToArray(),
            AxisY = part.AxisY.ToArray(),
            BboxMin = part.BboxMin.ToArray(),
            BboxMax = part.BboxMax.ToArray(),
            SolidVertices = part.SolidVertices.Select(static vertex => vertex.ToArray()).ToList(),
            ViewHull = part.ViewHull.Select(static vertex => vertex.ToArray()).ToList(),
            SolidGeometryComplete = part.SolidGeometryComplete,
            Type = part.Type,
            Name = part.Name,
            PartPos = part.PartPos,
            Profile = part.Profile,
            Material = part.Material,
            MaterialType = part.MaterialType,
            PartPrefix = part.PartPrefix,
            Role = part.Role
        };
    }

    private static BoltGroupGeometry CloneBolt(BoltGroupGeometry bolt)
    {
        return new BoltGroupGeometry
        {
            ModelId = bolt.ModelId,
            Shape = bolt.Shape,
            BoltType = bolt.BoltType,
            BoltStandard = bolt.BoltStandard,
            BoltSize = bolt.BoltSize,
            FirstPosition = bolt.FirstPosition.ToArray(),
            SecondPosition = bolt.SecondPosition.ToArray(),
            BboxMin = bolt.BboxMin.ToArray(),
            BboxMax = bolt.BboxMax.ToArray(),
            PartToBeBoltedId = bolt.PartToBeBoltedId,
            PartToBoltToId = bolt.PartToBoltToId,
            OtherPartIds = bolt.OtherPartIds.ToList(),
            Positions = bolt.Positions
                .Select(static position => new BoltPointGeometry
                {
                    Index = position.Index,
                    Point = position.Point.ToArray()
                })
                .ToList()
        };
    }
}
