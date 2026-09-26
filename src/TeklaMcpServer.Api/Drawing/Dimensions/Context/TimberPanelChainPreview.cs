using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Read-only chain proposal for straight timber wall panels.</summary>
internal static class TimberPanelChainPreview
{
    private const double PositionTolerance = 0.5;
    private const double TouchTolerance = 1.0;
    private const double ContactPositionTolerance = 0.001;

    internal sealed class PreviewRow(string side, object[] chains)
    {
        public string side { get; } = side;
        public object[] chains { get; } = chains;
    }

    internal sealed class PreviewResult(PreviewRow[] rows, int[][] contactFallbackPairs,
        int[] unlocatedModelIds, int[] missingSupportModelIds, int[] tiltedPartIds, string contactStatus)
        : IEnumerable<PreviewRow>
    {
        public PreviewRow[] Rows { get; } = rows;
        public int[][] ContactFallbackPairs { get; } = contactFallbackPairs;
        public int[] UnlocatedModelIds { get; } = unlocatedModelIds;
        public int[] MissingSupportModelIds { get; } = missingSupportModelIds;
        public int[] TiltedPartIds { get; } = tiltedPartIds;
        public string ContactStatus { get; } = contactStatus;
        public IEnumerator<PreviewRow> GetEnumerator() => ((IEnumerable<PreviewRow>)Rows).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Member(int id, double minX, double maxX, double minY, double maxY, bool vertical, bool tilted)
    {
        public int Id { get; } = id;
        public double MinX { get; } = minX;
        public double MaxX { get; } = maxX;
        public double MinY { get; } = minY;
        public double MaxY { get; } = maxY;
        public bool Vertical { get; } = vertical;
        public bool Tilted { get; } = tilted;
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    private sealed class MemberGroup(Member[] members)
    {
        public Member[] Members { get; } = members;
        public double MinX => Members.Min(m => m.MinX);
        public double MaxX => Members.Max(m => m.MaxX);
        public double MinY => Members.Min(m => m.MinY);
        public double MaxY => Members.Max(m => m.MaxY);
        public int[] Ids => Members.Select(m => m.Id).ToArray();
    }

    private sealed class Candidate(double coordinate, int[] owners, bool coordinateOnly = false)
    {
        public double Coordinate { get; } = coordinate;
        public int[] Owners { get; } = owners;
        public bool CoordinateOnly { get; } = coordinateOnly;
    }

    private sealed class PickedPoint(DimensionPoint[] points, int[] locatedIds, int[] droppedIds, int[] missingIds)
    {
        public DimensionPoint[] Points { get; } = points;
        public int[] LocatedIds { get; } = locatedIds;
        public int[] DroppedIds { get; } = droppedIds;
        public int[] MissingIds { get; } = missingIds;
        public double[] Coordinates(bool alongX) => Points.Select(p => alongX ? p.X : p.Y).ToArray();
    }

    public static PreviewResult Build(DimensionPointCatalog catalog, GeometryGroup group,
        IReadOnlyCollection<int> includedIds, double minimumSegment,
        Func<ViewContactCandidatePointsResult?> getContacts)
    {
        if (group.Extent == null)
            return new PreviewResult(Rows(side => [Empty(side, "location", "panel outline is empty")]),
                [], [], [], [], "not-available");

        var members = ReadMembers(group, includedIds);
        var vertical = members.Where(m => m.Vertical && !m.Tilted).ToArray();
        var horizontal = members.Where(m => !m.Vertical && !m.Tilted).ToArray();
        var tiltedIds = members.Where(m => m.Tilted).Select(m => m.Id).ToArray();
        var unresolvedIds = includedIds.Except(members.Select(m => m.Id)).ToArray();
        var touching = FindTouchingPairs(vertical);
        var contacts = touching.Count == 0 ? null : getContacts();
        var contactCheckComplete = IsContactCheckComplete(contacts);
        var confirmedPairs = contactCheckComplete ? ConfirmedPairs(contacts!, vertical, catalog) : null;
        var groups = GroupVerticals(vertical, touching);
        var contactFallbackPairs = contactCheckComplete
            ? touching.Where(pair => !confirmedPairs!.Contains(Order(pair.A, pair.B)))
                .Select(pair => new[] { pair.A, pair.B }).ToArray()
            : Array.Empty<int[]>();
        var panel = group.Extent;
        var outline = OutlineVertices(group);

        var bottom = BuildX(catalog, DimensionChainSide.Bottom, groups, vertical, panel, outline, minimumSegment);
        var top = BuildX(catalog, DimensionChainSide.Top, groups, vertical, panel, outline, minimumSegment);
        var topContained = CoordinatesContained(top.Coordinates(true), bottom.Coordinates(true));
        var bottomContained = CoordinatesContained(bottom.Coordinates(true), top.Coordinates(true));
        var left = BuildY(catalog, DimensionChainSide.Left, vertical, horizontal, groups, outline, minimumSegment);
        var right = BuildY(catalog, DimensionChainSide.Right, vertical, horizontal, groups, outline, minimumSegment);
        var leftContained = CoordinatesContained(left.Coordinates(false), right.Coordinates(false));
        var rightContained = CoordinatesContained(right.Coordinates(false), left.Coordinates(false));
        var contactStatus = touching.Count == 0 ? "not-requested"
            : contactCheckComplete && contactFallbackPairs.Length == 0 ? "complete-contact-check"
            : contactCheckComplete ? "complete-contact-check-geometry-fallback"
            : "geometry-fallback-contact-result-incomplete";

        var suppressTop = topContained;
        var suppressBottom = bottomContained && !topContained;
        var suppressLeft = leftContained && !rightContained;
        var suppressRight = rightContained;
        var rows = new[] {
            new PreviewRow("Bottom", [suppressBottom
                    ? Empty(DimensionChainSide.Bottom, "location", "all X positions are covered by Top")
                    : Location(DimensionChainSide.Bottom, bottom, minimumSegment),
                Overall(DimensionChainSide.Bottom, catalog, panel.MinX, panel.MaxX)]),
            new PreviewRow("Top", [suppressTop
                    ? Empty(DimensionChainSide.Top, "location", "all X positions are covered by Bottom")
                    : Location(DimensionChainSide.Top, top, minimumSegment)]),
            new PreviewRow("Left", [suppressLeft
                    ? Empty(DimensionChainSide.Left, "location", "all Y positions are covered by Right")
                    : Location(DimensionChainSide.Left, left, minimumSegment)]),
            new PreviewRow("Right", [suppressRight
                    ? Empty(DimensionChainSide.Right, "location", "all Y positions are covered by Left")
                    : Location(DimensionChainSide.Right, right, minimumSegment)])
        };

        var allChains = new[] { bottom, top, left, right };
        var allLocated = allChains.SelectMany(chain => chain.LocatedIds).ToHashSet();
        var allMissing = allChains.SelectMany(chain => chain.MissingIds).Distinct().ToArray();
        var unlocated = tiltedIds.Concat(unresolvedIds).Concat(allMissing).Except(allLocated).Distinct().ToArray();
        return new PreviewResult(rows, contactFallbackPairs, unlocated, allMissing, tiltedIds, contactStatus);
    }

    private static PreviewRow[] Rows(Func<DimensionChainSide, object[]> chains) =>
        Enum.GetValues(typeof(DimensionChainSide)).Cast<DimensionChainSide>()
            .Select(side => new PreviewRow(side.ToString(), chains(side))).ToArray();

    private static PickedPoint BuildX(DimensionPointCatalog catalog, DimensionChainSide side,
        MemberGroup[] groups, Member[] vertical, GeometryGroupExtent panel, (double X, double Y)[] outline, double minimumSegment)
    {
        var ordered = groups.OrderBy(g => g.MinX).ToArray();
        // The end groups are matched against the extremes of this side, not the panel's overall extent: a
        // corner of the outline that sticks out by a millimetre on the other side must not hide an end group.
        var (edgeMin, edgeMax) = OuterExtremes(catalog, side, alongX: true, panel.MinX, panel.MaxX);
        var leftEnd = ordered.FirstOrDefault(g => Math.Abs(g.MinX - edgeMin) <= PositionTolerance);
        var rightEnd = ordered.LastOrDefault(g => Math.Abs(g.MaxX - edgeMax) <= PositionTolerance);
        if (leftEnd == null || rightEnd == null)
            return BuildXFromColumns(catalog, side, vertical, panel, outline, minimumSegment);

        var candidates = new List<Candidate> { new(edgeMin, leftEnd.Ids) };
        Add(leftEnd.MaxX, leftEnd.Ids);
        foreach (var item in ordered.Where(g => g != leftEnd && g != rightEnd)) Add(item.MinX, item.Ids);
        if (rightEnd != leftEnd) Add(rightEnd.MinX, rightEnd.Ids);
        Add(edgeMax, rightEnd.Ids);
        foreach (var x in OutlineCoordinates(outline, side, alongX: true)) Add(x, []);
        return Pick(catalog, side, candidates, alongX: true, minimumSegment);

        void Add(double x, int[] owners)
        {
            if (candidates.Any(c => Math.Abs(c.Coordinate - x) <= PositionTolerance)) return;
            candidates.Add(new Candidate(x, owners));
        }
    }

    private static PickedPoint BuildXFromColumns(DimensionPointCatalog catalog, DimensionChainSide side,
        Member[] vertical, GeometryGroupExtent panel, (double X, double Y)[] outline, double minimumSegment)
    {
        var columns = new List<List<Member>>();
        foreach (var member in vertical.OrderBy(m => m.MinX).ThenBy(m => m.MinY))
        {
            var column = columns.FirstOrDefault(items =>
                Math.Abs(items.Average(item => item.MinX) - member.MinX) <= PositionTolerance);
            if (column == null) columns.Add([member]);
            else column.Add(member);
        }

        var (edgeMin, edgeMax) = OuterExtremes(catalog, side, alongX: true, panel.MinX, panel.MaxX);
        var candidates = new List<Candidate> { new(edgeMin, [], coordinateOnly: true) };
        foreach (var column in columns)
        {
            var x = column.Min(member => member.MinX);
            if (x <= edgeMin + PositionTolerance || x >= edgeMax - PositionTolerance) continue;
            candidates.Add(new Candidate(x, column.Select(member => member.Id).Distinct().ToArray(), coordinateOnly: true));
        }
        candidates.Add(new Candidate(edgeMax, [], coordinateOnly: true));
        candidates.AddRange(OutlineCoordinates(outline, side, alongX: true)
            .Select(x => new Candidate(x, [], coordinateOnly: true)));
        return Pick(catalog, side, candidates, alongX: true, minimumSegment);
    }

    private static PickedPoint BuildY(DimensionPointCatalog catalog, DimensionChainSide side,
        Member[] vertical, Member[] horizontal, MemberGroup[] groups, (double X, double Y)[] outline, double minimumSegment)
    {
        if (vertical.Length == 0 || groups.Length == 0) return new([], [], [], []);
        var end = side == DimensionChainSide.Left ? groups.OrderBy(g => g.MinX).First() : groups.OrderByDescending(g => g.MaxX).First();
        var lowest = vertical.Min(m => m.MinY);
        var lowestHorizontal = horizontal.Length == 0 ? double.NaN : horizontal.Min(m => m.MinY);
        var highestHorizontal = horizontal.Length == 0 ? double.NaN : horizontal.Max(m => m.MaxY);
        var candidates = new List<Candidate> {
            new(lowest, vertical.Where(m => Math.Abs(m.MinY - lowest) <= PositionTolerance).Select(m => m.Id).ToArray())
        };
        foreach (var member in horizontal)
        {
            // Use one face per member: the bottom exterior face, top exterior face,
            // or the lower face for an intermediate horizontal member.
            var coordinate = Math.Abs(member.MinY - lowestHorizontal) <= PositionTolerance
                ? member.MinY
                : Math.Abs(member.MaxY - highestHorizontal) <= PositionTolerance
                    ? member.MaxY
                    : member.MinY;
            candidates.Add(new Candidate(coordinate, [member.Id]));
        }
        // The top of the end posts is the lower face of the member they stand under when it coincides:
        // that face is already a position (one face per member), so it is not added a second time.
        if (!horizontal.Any(m => Math.Abs(m.MinY - end.MaxY) <= PositionTolerance))
            candidates.Add(new Candidate(end.MaxY, end.Ids));
        candidates.AddRange(OutlineCoordinates(outline, side, alongX: false)
            .Select(y => new Candidate(y, [], coordinateOnly: true)));
        return Pick(catalog, side, candidates, alongX: false, minimumSegment);
    }

    private static PickedPoint Pick(DimensionPointCatalog catalog, DimensionChainSide side,
        IEnumerable<Candidate> candidates, bool alongX, double minimumSegment)
    {
        var points = new List<DimensionPoint>();
        var owners = new List<int>();
        var missing = new List<int>();
        foreach (var candidate in candidates.OrderBy(c => c.Coordinate))
        {
            if (points.Count > 0 && Math.Abs(Coordinate(points[points.Count - 1], alongX) - candidate.Coordinate) <= PositionTolerance)
            {
                var existing = points[points.Count - 1];
                var representedOwners = candidate.Owners.Where(id => Owns(existing, id)).ToArray();
                if (representedOwners.Length == 0) missing.AddRange(candidate.Owners);
                else owners.AddRange(representedOwners);
                continue;
            }
            var point = FindPoint(catalog.LinePoints(side), side, alongX, candidate);
            if (point == null)
            {
                missing.AddRange(candidate.Owners);
                continue;
            }
            points.Add(point);
            owners.AddRange(candidate.CoordinateOnly ? candidate.Owners : candidate.Owners.Where(id => Owns(point, id)));
        }

        var dropped = new List<int>();
        for (var i = points.Count - 2; i > 0; i--)
        {
            var gap = Coordinate(points[i + 1], alongX) - Coordinate(points[i], alongX);
            if (gap >= minimumSegment) continue;
            dropped.AddRange(PartIds(points[i]));
            points.RemoveAt(i);
        }
        var located = points.SelectMany(PartIds).Concat(owners).Distinct().ToArray();
        return new(points.ToArray(), located, dropped.Distinct().ToArray(), missing.Distinct().ToArray());
    }

    // The point of a position that lies farthest toward the dimension line, so the witness line does not
    // run across the panel; the exact coordinate only breaks ties.
    private static DimensionPoint? FindPoint(IEnumerable<DimensionPoint> line, DimensionChainSide side, bool alongX, Candidate candidate)
    {
        var owners = candidate.Owners.ToHashSet();
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        return line.Where(p => Math.Abs(Coordinate(p, alongX) - candidate.Coordinate) <= PositionTolerance
                && (candidate.CoordinateOnly || p.Parents.Any(parent => parent.ModelId.HasValue && owners.Contains(parent.ModelId.Value))))
            .OrderByDescending(p => outer * (alongX ? p.Y : p.X))
            .ThenBy(p => Math.Abs(Coordinate(p, alongX) - candidate.Coordinate))
            .FirstOrDefault();
    }

    // The extremes of a side taken from the half of the panel on the dimension line's side: a raked or
    // stepped panel has its highest corner on the other side, and a chain must not start there.
    // Every point of the panel outline polygon is a position: the corners of a trapezoid or any other
    // shape are what gives the panel its form. The polygon comes from the group's boundary shapes.
    private static (double X, double Y)[] OutlineVertices(GeometryGroup group) =>
        group.BoundaryShapes.Where(shape => !shape.IsHole).SelectMany(shape => shape.Shape.Points)
            .Select(point => (point.X, point.Y)).Distinct().ToArray();

    // The outline coordinates a side carries: X of the vertices in the half of the panel on the side of
    // the dimension line for Top and Bottom, Y of the vertices on the left or right half for the others.
    private static IEnumerable<double> OutlineCoordinates((double X, double Y)[] outline, DimensionChainSide side, bool alongX)
    {
        if (outline.Length == 0) return [];
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        if (alongX)
        {
            var middle = (outline.Max(v => v.Y) + outline.Min(v => v.Y)) / 2;
            return outline.Where(v => outer * (v.Y - middle) >= -PositionTolerance).Select(v => v.X);
        }
        var middleX = (outline.Max(v => v.X) + outline.Min(v => v.X)) / 2;
        return outline.Where(v => outer * (v.X - middleX) >= -PositionTolerance).Select(v => v.Y);
    }

    private static (double Min, double Max) OuterExtremes(DimensionPointCatalog catalog, DimensionChainSide side,
        bool alongX, double panelMin, double panelMax)
    {
        var line = catalog.LinePoints(side);
        if (line.Count == 0) return (panelMin, panelMax);
        double Across(DimensionPoint p) => alongX ? p.Y : p.X;
        var middle = (line.Max(Across) + line.Min(Across)) / 2;
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        var half = line.Where(p => outer * (Across(p) - middle) >= -PositionTolerance).ToArray();
        if (half.Length == 0) return (panelMin, panelMax);
        return (half.Min(p => Coordinate(p, alongX)), half.Max(p => Coordinate(p, alongX)));
    }

    private static object Location(DimensionChainSide side, PickedPoint chain, double minimumSegment)
    {
        var coordinates = chain.Coordinates(side is DimensionChainSide.Top or DimensionChainSide.Bottom);
        var segments = coordinates.Zip(coordinates.Skip(1), (a, b) => Math.Round(b - a, 3)).ToArray();
        return new {
            side = side.ToString(), kind = "location", pointIds = chain.Points.Select(p => p.Id).ToArray(),
            segments, points = chain.Points.Select((p, i) => new { pointId = p.Id, partIds = PartIds(p), role = i == 0 || i == chain.Points.Length - 1 ? "extreme" : "member-face" }).ToArray(),
            droppedShortPartIds = chain.DroppedIds,
            minimumSegmentViewUnits = minimumSegment,
            incomplete = chain.MissingIds.Length > 0 || chain.Points.Length < 2
        };
    }

    private static object Overall(DimensionChainSide side, DimensionPointCatalog catalog, double panelMin, double panelMax)
    {
        var line = catalog.LinePoints(side);
        var (min, max) = OuterExtremes(catalog, side, alongX: true, panelMin, panelMax);
        var outer = side is DimensionChainSide.Top ? 1 : -1;
        var first = line.Where(p => Math.Abs(p.X - min) <= PositionTolerance).OrderByDescending(p => outer * p.Y).FirstOrDefault();
        var last = line.Where(p => Math.Abs(p.X - max) <= PositionTolerance).OrderByDescending(p => outer * p.Y).FirstOrDefault();
        if (first == null || last == null) return Empty(side, "overall", "panel extremes are not supported by existing dimension points");
        return new { side = side.ToString(), kind = "overall", pointIds = new[] { first.Id, last.Id }, segments = new[] { Math.Round(max - min, 3) }, points = Array.Empty<object>(), row = "second" };
    }

    private static object Empty(DimensionChainSide side, string kind, string note) => new {
        side = side.ToString(), kind, pointIds = Array.Empty<string>(), segments = Array.Empty<double>(), points = Array.Empty<object>(), note
    };

    private static Member[] ReadMembers(GeometryGroup group, IReadOnlyCollection<int> includedIds) =>
        group.Shapes.Where(s => !s.IsHole && s.ModelId.HasValue && includedIds.Contains(s.ModelId.Value))
            .GroupBy(s => s.ModelId!.Value).Select(g => {
                var vertices = g.SelectMany(s => s.Shape.Points).ToArray();
                if (vertices.Length == 0) return null;
                var minX = vertices.Min(p => p.X); var maxX = vertices.Max(p => p.X);
                var minY = vertices.Min(p => p.Y); var maxY = vertices.Max(p => p.Y);
                var edges = g.SelectMany(s => s.Shape.Points.Zip(s.Shape.Points.Skip(1).Append(s.Shape.Points[0]),
                    (a, b) => (dx: Math.Abs(b.X - a.X), dy: Math.Abs(b.Y - a.Y)))).ToArray();
                var isVertical = maxY - minY > maxX - minX;
                // A member whose long faces are straight is not tilted just because an end is cut at
                // an angle (a stud of a raked wall): its sides still give exact positions.
                var straightFaces = isVertical
                    ? edges.Count(e => e.dx <= PositionTolerance && e.dy >= (maxY - minY) / 2)
                    : edges.Count(e => e.dy <= PositionTolerance && e.dx >= (maxX - minX) / 2);
                var tilted = edges.Any(edge => edge.dx > PositionTolerance && edge.dy > PositionTolerance)
                    && straightFaces < 2;
                return new Member(g.Key, minX, maxX, minY, maxY, maxY - minY > maxX - minX, tilted);
            }).Where(m => m != null).Cast<Member>().ToArray();

    private static List<(int A, int B)> FindTouchingPairs(Member[] vertical)
    {
        var pairs = new List<(int, int)>();
        for (var i = 0; i < vertical.Length; i++)
        for (var j = i + 1; j < vertical.Length; j++)
        {
            var a = vertical[i]; var b = vertical[j];
            var gap = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            if (gap <= TouchTolerance && Math.Min(a.MaxY, b.MaxY) > Math.Max(a.MinY, b.MinY)) pairs.Add((a.Id, b.Id));
        }
        return pairs;
    }

    private static MemberGroup[] GroupVerticals(Member[] vertical, List<(int A, int B)> touching)
    {
        var union = new UnionFind(vertical.Select(m => m.Id));
        foreach (var (a, b) in touching)
            union.Join(a, b);
        return vertical.GroupBy(m => union.Find(m.Id)).Select(g => new MemberGroup(g.ToArray())).ToArray();
    }

    private static bool IsContactCheckComplete(ViewContactCandidatePointsResult? result) => result != null
        && result.IsComplete && result.SearchComplete && result.Error == null && result.Unread.Count == 0
        && result.Unflattened.Count == 0 && result.Unresolved.Count == 0;

    private static HashSet<(int, int)> ConfirmedPairs(ViewContactCandidatePointsResult result,
        Member[] vertical, DimensionPointCatalog catalog)
    {
        var verticalById = vertical.ToDictionary(m => m.Id);
        var confirmed = new HashSet<(int, int)>();
        var shapes = result.Points.Where(p => p.ModelObjectIds.Count == 2
            && p.Reason.Values.TryGetValue("contactKind", out var kind) && kind == ContactKind.FaceToFace.ToString()
            && p.Reason.Values.TryGetValue("contactState", out var state) && state == ContactState.Touching.ToString())
            .GroupBy(p => string.Join(",", p.ModelObjectIds.OrderBy(id => id)) + ":"
                + (p.Reason.Values.TryGetValue("shapeId", out var shape) ? shape : p.Anchor.Id));
        foreach (var shape in shapes)
        {
            var points = shape.ToArray();
            var ids = points[0].ModelObjectIds.OrderBy(id => id).ToArray();
            if (ids.Length != 2 || !verticalById.TryGetValue(ids[0], out var a) || !verticalById.TryGetValue(ids[1], out var b)) continue;
            var ySpan = points.Max(p => p.Point[1]) - points.Min(p => p.Point[1]);
            var pairIds = ids.ToHashSet();
            var sharedX = points.Average(p => p.Point[0]);
            var isVerticalFace = points.All(p => Math.Abs(p.Point[0] - points[0].Point[0]) <= ContactPositionTolerance);
            var existingPosition = catalog.AllPoints.Any(p => Math.Abs(p.X - sharedX) <= ContactPositionTolerance
                && p.Parents.Any(parent => parent.ModelId.HasValue && pairIds.Contains(parent.ModelId.Value)));
            if (isVerticalFace && ySpan >= Math.Min(a.Height, b.Height) / 2 && existingPosition)
                confirmed.Add((ids[0], ids[1]));
        }
        return confirmed;
    }

    private static int[] PartIds(DimensionPoint p) => p.Parents.Where(parent => parent.ModelId.HasValue).Select(parent => parent.ModelId!.Value).Distinct().ToArray();
    private static bool Owns(DimensionPoint p, int id) => p.Parents.Any(parent => parent.ModelId == id);
    private static double Coordinate(DimensionPoint p, bool alongX) => alongX ? p.X : p.Y;
    private static bool CoordinatesContained(double[] candidate, double[] other) => candidate.Length > 0
        && candidate.All(value => other.Any(existing => Math.Abs(value - existing) <= PositionTolerance));
    private static (int, int) Order(int a, int b) => a < b ? (a, b) : (b, a);

    private sealed class UnionFind(IEnumerable<int> ids)
    {
        private readonly Dictionary<int, int> _parent = ids.ToDictionary(id => id, id => id);
        public int Find(int id) { if (_parent[id] != id) _parent[id] = Find(_parent[id]); return _parent[id]; }
        public void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) _parent[b] = a; }
    }
}
