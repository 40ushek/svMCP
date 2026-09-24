using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    private static DimensionWriteState WriteVerifiedDimension(
        Tekla.Structures.Drawing.Drawing drawing, ViewBase view, double[] points,
        Vector direction, double distance, StraightDimensionSet.StraightDimensionSetAttributes attributes,
        StraightDimensionSet? original = null)
    {
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = true;
        try
        {
            var expectedRowType = attributes.DimensionType;
            var viewId = view.GetIdentifier().ID;
            var originalId = original?.GetIdentifier().ID;
            double? observedDistance = null;
            double? initialDistance = null;
            DimensionRenderedLineResult? renderedLine = null;
            var state = DimensionWriteProtocol.Execute(
                () => new StraightDimensionSetHandler()
                    .CreateDimensionSet(view, ToPointList(points), direction, distance, attributes)
                    ?.GetIdentifier().ID ?? 0,
                () => drawing.CommitChanges("(MCP) Verified dimension write"),
                id =>
                {
                    var read = FindDimensionSet(drawing, id);
                    if (read == null || !read.Select()) return "Replacement cannot be read back";
                    if (read.GetView()?.GetIdentifier().ID != viewId) return "Replacement is in a different view";
                    if (!DimensionWriteProtocol.Finite(read.Distance)) return "Replacement offset is not finite";
                    observedDistance = read.Distance;
                    initialDistance ??= observedDistance;
                    if (Math.Abs(read.Distance - distance) > 1e-6)
                    {
                        read.Distance = distance;
                        if (!read.Modify()) return "Replacement offset Modify() returned false";
                        if (!drawing.CommitChanges("(MCP) Dimension offset correction"))
                            return "Replacement offset CommitChanges() returned false";
                        read = FindDimensionSet(drawing, id);
                        if (read == null || !read.Select()) return "Corrected replacement cannot be read back";
                    }
                    if (!DimensionWriteProtocol.Finite(read.Distance) || Math.Abs(read.Distance - distance) > 1e-6)
                        return "Replacement offset differs from request";
                    observedDistance = read.Distance;
                    if (read.Attributes.DimensionType != expectedRowType) return "Replacement row type differs from request";
                    var actual = new List<(double X, double Y)>();
                    var links = new List<((double X, double Y) Start, (double X, double Y) End)>();
                    var segments = read.GetObjects();
                    var segmentCount = 0;
                    while (segments.MoveNext())
                    {
                        if (segments.Current is not StraightDimension segment) continue;
                        segmentCount++;
                        links.Add(((segment.StartPoint.X, segment.StartPoint.Y), (segment.EndPoint.X, segment.EndPoint.Y)));
                        var up = segment.UpDirection;
                        var expectedLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
                        var actualLength = Math.Sqrt(up.X * up.X + up.Y * up.Y + up.Z * up.Z);
                        var cosine = (up.X * direction.X + up.Y * direction.Y + up.Z * direction.Z) / (expectedLength * actualLength);
                        if (!DimensionWriteProtocol.Finite(cosine) || cosine < 1 - 1e-6)
                            return "Replacement direction/side differs from request";
                        foreach (var p in new[] { segment.StartPoint, segment.EndPoint })
                        {
                            if (!DimensionWriteProtocol.Finite(p.X) || !DimensionWriteProtocol.Finite(p.Y))
                                return "Replacement contains unreadable points";
                            if (!actual.Any(a => Math.Abs(a.X - p.X) < 1e-6 && Math.Abs(a.Y - p.Y) < 1e-6))
                                actual.Add((p.X, p.Y));
                        }
                    }
                    var pointError = DimensionWriteVerification.CheckPoints(points, actual, segmentCount);
                    if (pointError != null) return pointError;
                    var datumError = expectedRowType == DimensionSetBaseAttributes.DimensionTypes.Relative
                        ? null : DimensionWriteVerification.CheckDatum(points[0], points[1], links);
                    if (datumError != null) return datumError;
                    renderedLine = DimensionRenderedLineVerification.Read(view, read, points, direction, distance);
                    // An observed mismatch follows the existing compensation path, before
                    // deletion of an original. Unavailable observation keeps its own status.
                    return renderedLine.Status == "mismatch" ? renderedLine.Reason : null;
                },
                original == null ? null : () => original.Delete(),
                originalId == null ? null : () => FindDimensionSet(drawing, originalId.Value) == null,
                id => FindDimensionSet(drawing, id)?.Delete() == true,
                id => FindDimensionSet(drawing, id) == null);
            state.ObservedDistance = observedDistance;
            state.InitialDistance = initialDistance;
            state.RenderedLine = renderedLine;
            return state;
        }
        finally { DrawingEnumeratorBase.AutoFetch = previousAutoFetch; }
    }
}

internal static class DimensionWriteVerification
{
    internal static string? CheckDatum(double x, double y,
        IReadOnlyList<((double X, double Y) Start, (double X, double Y) End)> segments)
    {
        bool Same((double X, double Y) a, (double X, double Y) b) =>
            Math.Abs(a.X - b.X) <= 0.01 && Math.Abs(a.Y - b.Y) <= 0.01;
        var remaining = segments.ToList();
        var starts = remaining.Where(s => !remaining.Any(other => Same(other.End, s.Start))).ToList();
        if (starts.Count != 1 || !Same(starts[0].Start, (x, y)))
            return "Running datum is unverified or differs from requested first point";
        var cursor = starts[0].Start;
        while (remaining.Count > 0)
        {
            var next = remaining.Where(s => Same(s.Start, cursor)).ToList();
            if (next.Count != 1) return "Running datum is unverified: branched or disconnected segment path";
            cursor = next[0].End;
            remaining.Remove(next[0]);
        }
        return null;
    }

    // Does not infer a running-dimension datum from normalized read-back order.
    internal static string? CheckPoints(double[] expected, IReadOnlyList<(double X, double Y)> actual, int segments)
    {
        if (actual.Count != expected.Length / 3 || segments != actual.Count - 1)
            return "Replacement point/segment count differs from request";
        var remaining = actual.ToList();
        for (var i = 0; i < expected.Length; i += 3)
        {
            var x = expected[i]; var y = expected[i + 1];
            var index = remaining.FindIndex(p => Math.Abs(p.X - x) <= 0.01 && Math.Abs(p.Y - y) <= 0.01);
            if (index < 0) return "Replacement points differ from request";
            remaining.RemoveAt(index);
        }
        return null;
    }
}
