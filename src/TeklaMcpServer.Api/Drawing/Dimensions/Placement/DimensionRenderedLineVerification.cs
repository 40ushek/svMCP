using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing.Dimensions;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DimensionRenderedLineResult
{
    public string Status { get; set; } = "not verified";
    public string? Reason { get; set; }
    public double? ExpectedOffsetProjection { get; set; }
    public List<double> ObservedOffsetProjections { get; set; } = new();
    public string Units { get; set; } = "view units";
}

internal static class DimensionRenderedLineVerification
{
    internal static DimensionRenderedLineResult Read(ViewBase viewBase, StraightDimensionSet set,
        double[] points, Vector direction, double distance)
    {
        try
        {
            if (viewBase is not Tekla.Structures.Drawing.View view)
                return Unknown("Owner is not a model drawing view");
            if (!view.Select()) return Unknown("View attributes unavailable");
            var shortening = ViewShorteningAttributesReader.Read(view);
            if (shortening.CutParts || shortening.CutSkewParts)
                return Unknown("View shortening is enabled; presentation mapping is not verified");
            var scale = view.Attributes.Scale;
            if (!DimensionWriteProtocol.Finite(scale) || scale <= 0) return Unknown("Invalid scale");
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length <= 0 || Math.Abs(direction.Z) > 1e-8) return Unknown("Unsupported dimension direction");
            var up = (X: direction.X / length, Y: direction.Y / length);
            var xy = Enumerable.Range(0, points.Length / 3).Select(i => (points[3 * i], points[3 * i + 1])).ToArray();
            if (!DimensionProjectionHelper.TryBaseProjection(xy, up, out var baseProjection))
                return Unknown("Only axis-aligned dimensions with an unambiguous base are supported");
            var expected = baseProjection + distance;
            using var connection = new PresentationConnection();
            var observations = new List<double?>();
            var segments = set.GetObjects();
            while (segments.MoveNext())
            {
                if (segments.Current is not StraightDimension segment) continue;
                var presentation = connection.Service.GetObjectPresentation(segment.GetIdentifier().ID);
                var lines = new List<(double X1, double Y1, double X2, double Y2)>();
                Collect(presentation?.Primitives, scale, lines, 0);
                observations.Add(IdentifyLine(lines, (segment.StartPoint.X, segment.StartPoint.Y),
                    (segment.EndPoint.X, segment.EndPoint.Y), up));
            }
            return Compare(expected, observations);
        }
        catch (Exception ex) { return Unknown("Presentation read failed: " + ex.Message); }
    }

    // Identify by orientation and measured span, never by proximity to the expected offset.
    // Fragmented lines or several candidates at different offsets remain unverified.
    internal static double? IdentifyLine(IEnumerable<(double X1, double Y1, double X2, double Y2)> lines,
        (double X, double Y) start, (double X, double Y) end, (double X, double Y) up)
    {
        const double tolerance = DimensionPlacementSettings.VerificationToleranceViewUnits;
        var axis = (X: -up.Y, Y: up.X);
        double Along(double x, double y) => x * axis.X + y * axis.Y;
        double Offset(double x, double y) => x * up.X + y * up.Y;
        var min = Math.Min(Along(start.X, start.Y), Along(end.X, end.Y));
        var max = Math.Max(Along(start.X, start.Y), Along(end.X, end.Y));
        var candidates = lines.Where(l => new[] { l.X1, l.Y1, l.X2, l.Y2 }.All(DimensionWriteProtocol.Finite))
            .Where(l => Math.Abs(Offset(l.X1, l.Y1) - Offset(l.X2, l.Y2)) <= 1e-5)
            .Where(l => Math.Abs(Math.Min(Along(l.X1, l.Y1), Along(l.X2, l.Y2)) - min) <= tolerance
                && Math.Abs(Math.Max(Along(l.X1, l.Y1), Along(l.X2, l.Y2)) - max) <= tolerance)
            .Select(l => Offset(l.X1, l.Y1)).ToArray();
        if (candidates.Length == 0 || candidates.Max() - candidates.Min() > 1e-5) return null;
        return candidates[0];
    }

    internal static DimensionRenderedLineResult Compare(double expected, IReadOnlyList<double?> observations)
    {
        var known = observations.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var mismatch = known.Any(x => !DimensionWriteProtocol.Finite(x)
            || Math.Abs(x - expected) > DimensionPlacementSettings.VerificationToleranceViewUnits);
        return new DimensionRenderedLineResult {
            Status = mismatch ? "mismatch" : observations.Count == 0 || known.Count != observations.Count ? "not verified" : "matched",
            Reason = mismatch ? "Rendered segment line differs from requested placement"
                : observations.Count == 0 || known.Count != observations.Count ? "Not every segment has an unambiguous presentation line" : null,
            ExpectedOffsetProjection = expected, ObservedOffsetProjections = known
        };
    }

    private static DimensionRenderedLineResult Unknown(string reason) => new() { Reason = reason };

    private static void Collect(IList<PrimitiveBase>? primitives, double scale,
        List<(double, double, double, double)> lines, int depth)
    {
        if (primitives == null || depth > 8) return;
        foreach (var primitive in primitives)
        {
            if (primitive is LinePrimitive line)
                lines.Add((line.StartPoint.X * scale, line.StartPoint.Y * scale, line.EndPoint.X * scale, line.EndPoint.Y * scale));
            else if (primitive is Segment segment) Collect(segment.Primitives, scale, lines, depth + 1);
            else if (primitive is PrimitiveGroup group) Collect(group.Primitives, scale, lines, depth + 1);
        }
    }
}
