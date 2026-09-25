using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Read-only chain proposal for straight timber wall panels.</summary>
internal static class TimberPanelChainPreview
{
    private const double PositionTolerance = 0.5;
    private const double TouchTolerance = 1.0;

    internal sealed class PreviewRow(string side, object[] chains)
    {
        public string side { get; } = side;
        public object[] chains { get; } = chains;
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

    private sealed class Candidate(double coordinate, int[] owners)
    {
        public double Coordinate { get; } = coordinate;
        public int[] Owners { get; } = owners;
    }

    private sealed class PickedPoint(DimensionPoint[] points, int[] locatedIds, int[] droppedIds, int[] missingIds)
    {
        public DimensionPoint[] Points { get; } = points;
        public int[] LocatedIds { get; } = locatedIds;
        public int[] DroppedIds { get; } = droppedIds;
        public int[] MissingIds { get; } = missingIds;
        public double[] Coordinates(bool alongX) => Points.Select(p => alongX ? p.X : p.Y).ToArray();
    }

    public static PreviewRow[] Build(DimensionPointCatalog catalog, GeometryGroup group,
        IReadOnlyCollection<int> includedIds, double minimumSegment,
        Func<ViewContactCandidatePointsResult?> getContacts)
    {
        if (group.Extent == null)
            return Rows(side => [Empty(side, "location", "panel outline is empty")]);

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

        var bottom = BuildX(catalog, DimensionChainSide.Bottom, groups, panel, minimumSegment);
        var top = BuildX(catalog, DimensionChainSide.Top, groups, panel, minimumSegment);
        var topSame = SameCoordinates(bottom.Coordinates(true), top.Coordinates(true));
        var left = BuildY(catalog, DimensionChainSide.Left, vertical, horizontal, groups, minimumSegment);
        var right = BuildY(catalog, DimensionChainSide.Right, vertical, horizontal, groups, minimumSegment);
        var rightSame = SameCoordinates(left.Coordinates(false), right.Coordinates(false));
        var contactStatus = touching.Count == 0 ? "not-requested"
            : contactCheckComplete && contactFallbackPairs.Length == 0 ? "complete-contact-check"
            : contactCheckComplete ? "complete-contact-check-geometry-fallback"
            : "geometry-fallback-contact-result-incomplete";

        return [
            new PreviewRow("Bottom", [Location(DimensionChainSide.Bottom, bottom, minimumSegment, tiltedIds, unresolvedIds, contactStatus, contactFallbackPairs),
                Overall(DimensionChainSide.Bottom, catalog, panel.MinX, panel.MaxX)]),
            new PreviewRow("Top", [topSame ? Empty(DimensionChainSide.Top, "location", "same X positions as Bottom")
                : Location(DimensionChainSide.Top, top, minimumSegment, tiltedIds, unresolvedIds, contactStatus, contactFallbackPairs)]),
            new PreviewRow("Left", [Location(DimensionChainSide.Left, left, minimumSegment, tiltedIds, unresolvedIds, contactStatus, contactFallbackPairs)]),
            new PreviewRow("Right", [rightSame ? Empty(DimensionChainSide.Right, "location", "same Y positions as Left")
                : Location(DimensionChainSide.Right, right, minimumSegment, tiltedIds, unresolvedIds, contactStatus, contactFallbackPairs)])
        ];
    }

    private static PreviewRow[] Rows(Func<DimensionChainSide, object[]> chains) =>
        Enum.GetValues(typeof(DimensionChainSide)).Cast<DimensionChainSide>()
            .Select(side => new PreviewRow(side.ToString(), chains(side))).ToArray();

    private static PickedPoint BuildX(DimensionPointCatalog catalog, DimensionChainSide side,
        MemberGroup[] groups, GeometryGroupExtent panel, double minimumSegment)
    {
        var ordered = groups.OrderBy(g => g.MinX).ToArray();
        if (ordered.Length == 0) return new([], [], [], []);
        var leftEnd = ordered.FirstOrDefault(g => Math.Abs(g.MinX - panel.MinX) <= PositionTolerance);
        var rightEnd = ordered.LastOrDefault(g => Math.Abs(g.MaxX - panel.MaxX) <= PositionTolerance);
        if (leftEnd == null || rightEnd == null) return new([], [], [], ordered.SelectMany(g => g.Ids).Distinct().ToArray());

        var candidates = new List<Candidate> { new(panel.MinX, leftEnd.Ids) };
        Add(leftEnd.MaxX, leftEnd.Ids);
        foreach (var item in ordered.Where(g => g != leftEnd && g != rightEnd)) Add(item.MinX, item.Ids);
        if (rightEnd != leftEnd) Add(rightEnd.MinX, rightEnd.Ids);
        Add(panel.MaxX, rightEnd.Ids);
        return Pick(catalog, side, candidates, alongX: true, minimumSegment);

        void Add(double x, int[] owners)
        {
            if (candidates.Any(c => Math.Abs(c.Coordinate - x) <= PositionTolerance)) return;
            candidates.Add(new Candidate(x, owners));
        }
    }

    private static PickedPoint BuildY(DimensionPointCatalog catalog, DimensionChainSide side,
        Member[] vertical, Member[] horizontal, MemberGroup[] groups, double minimumSegment)
    {
        if (vertical.Length == 0 || groups.Length == 0) return new([], [], [], []);
        var end = side == DimensionChainSide.Left ? groups.OrderBy(g => g.MinX).First() : groups.OrderByDescending(g => g.MaxX).First();
        var lowest = vertical.Min(m => m.MinY);
        var candidates = new List<Candidate> {
            new(lowest, vertical.Where(m => Math.Abs(m.MinY - lowest) <= PositionTolerance).Select(m => m.Id).ToArray())
        };
        foreach (var member in horizontal)
        {
            candidates.Add(new Candidate(member.MinY, [member.Id]));
            candidates.Add(new Candidate(member.MaxY, [member.Id]));
        }
        candidates.Add(new Candidate(end.MaxY, end.Ids));
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
            var point = FindPoint(catalog.LinePoints(side), alongX, candidate);
            if (point == null)
            {
                missing.AddRange(candidate.Owners);
                continue;
            }
            points.Add(point);
            owners.AddRange(candidate.Owners.Where(id => Owns(point, id)));
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

    private static DimensionPoint? FindPoint(IEnumerable<DimensionPoint> line, bool alongX, Candidate candidate)
    {
        var owners = candidate.Owners.ToHashSet();
        return line.Where(p => Math.Abs(Coordinate(p, alongX) - candidate.Coordinate) <= PositionTolerance
                && p.Parents.Any(parent => parent.ModelId.HasValue && owners.Contains(parent.ModelId.Value)))
            .OrderBy(p => Math.Abs(Coordinate(p, alongX) - candidate.Coordinate))
            .ThenBy(p => alongX ? p.Y : p.X)
            .FirstOrDefault();
    }

    private static object Location(DimensionChainSide side, PickedPoint chain, double minimumSegment,
        int[] tiltedIds, int[] unresolvedIds, string contactStatus, int[][] contactFallbackPairs)
    {
        var unlocated = tiltedIds.Concat(unresolvedIds).Concat(chain.MissingIds).Except(chain.LocatedIds).Distinct().ToArray();
        var coordinates = chain.Coordinates(side is DimensionChainSide.Top or DimensionChainSide.Bottom);
        var segments = coordinates.Zip(coordinates.Skip(1), (a, b) => Math.Round(b - a, 3)).ToArray();
        return new {
            side = side.ToString(), kind = "location", pointIds = chain.Points.Select(p => p.Id).ToArray(),
            segments, points = chain.Points.Select((p, i) => new { pointId = p.Id, partIds = PartIds(p), role = i == 0 || i == chain.Points.Length - 1 ? "extreme" : "member-face" }).ToArray(),
            skippedPartIds = unlocated, unlocatedModelIds = unlocated, droppedShortPartIds = chain.DroppedIds,
            missingSupportModelIds = chain.MissingIds,
            tiltedPartIds = tiltedIds, minimumSegmentViewUnits = minimumSegment, contactStatus, contactFallbackPairs,
            incomplete = chain.MissingIds.Length > 0 || chain.Points.Length < 2
        };
    }

    private static object Overall(DimensionChainSide side, DimensionPointCatalog catalog, double min, double max)
    {
        var line = catalog.LinePoints(side);
        var first = line.Where(p => Math.Abs(p.X - min) <= PositionTolerance).OrderBy(p => p.Y).FirstOrDefault();
        var last = line.Where(p => Math.Abs(p.X - max) <= PositionTolerance).OrderByDescending(p => p.Y).FirstOrDefault();
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
                var tilted = g.SelectMany(s => s.Shape.Points.Zip(s.Shape.Points.Skip(1).Append(s.Shape.Points[0]),
                    (a, b) => (dx: Math.Abs(b.X - a.X), dy: Math.Abs(b.Y - a.Y))))
                    .Any(edge => edge.dx > PositionTolerance && edge.dy > PositionTolerance);
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
            var sharedX = points.Average(p => p.Point[0]);
            var pairIds = ids.ToHashSet();
            var isVerticalFace = points.All(p => p.Point[0] == points[0].Point[0]);
            var existingPosition = catalog.AllPoints.Any(p => p.X == sharedX
                && p.Parents.Any(parent => parent.ModelId.HasValue && pairIds.Contains(parent.ModelId.Value)));
            if (isVerticalFace && ySpan >= Math.Min(a.Height, b.Height) / 2 && existingPosition)
                confirmed.Add((ids[0], ids[1]));
        }
        return confirmed;
    }

    private static int[] PartIds(DimensionPoint p) => p.Parents.Where(parent => parent.ModelId.HasValue).Select(parent => parent.ModelId!.Value).Distinct().ToArray();
    private static bool Owns(DimensionPoint p, int id) => p.Parents.Any(parent => parent.ModelId == id);
    private static double Coordinate(DimensionPoint p, bool alongX) => alongX ? p.X : p.Y;
    private static bool SameCoordinates(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) <= PositionTolerance).All(x => x);
    private static (int, int) Order(int a, int b) => a < b ? (a, b) : (b, a);

    private sealed class UnionFind(IEnumerable<int> ids)
    {
        private readonly Dictionary<int, int> _parent = ids.ToDictionary(id => id, id => id);
        public int Find(int id) { if (_parent[id] != id) _parent[id] = Find(_parent[id]); return _parent[id]; }
        public void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) _parent[b] = a; }
    }
}
