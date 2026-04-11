using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal enum LeaderAnchorCandidateKind
{
    Nearest,
    ShiftedLeft,
    ShiftedRight,
}

internal sealed class LeaderAnchorCandidate
{
    public LeaderAnchorCandidateKind Kind { get; set; }
    public int EdgeIndex { get; set; }
    public double EdgeT { get; set; }
    public DrawingPointInfo? EdgePoint { get; set; }
    public DrawingPointInfo? AnchorPoint { get; set; }
    public double CornerDistance { get; set; }
    public double FarEdgeClearance { get; set; }
    public double LineLengthToLeaderEnd { get; set; }
}

internal static class LeaderAnchorCandidateGenerator
{
    public static List<LeaderAnchorCandidate> CreateCandidates(
        IReadOnlyList<double[]> polygon,
        LeaderSnapshot snapshot,
        double depthMm,
        double minFarEdgeClearanceMm)
    {
        if (polygon == null)
            throw new ArgumentNullException(nameof(polygon));
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var candidates = new List<LeaderAnchorCandidate>();
        if (polygon.Count < 3 || snapshot.AnchorPoint == null)
            return candidates;

        var referencePoint = snapshot.LeaderEndPoint ?? snapshot.InsertionPoint ?? snapshot.AnchorPoint;
        if (referencePoint == null ||
            !LeaderAnchorResolver.TryFindNearestEdgeHit(polygon, referencePoint.X, referencePoint.Y, out var hit))
        {
            return candidates;
        }

        var edgeDx = hit.EndX - hit.StartX;
        var edgeDy = hit.EndY - hit.StartY;
        var edgeLength = Math.Sqrt((edgeDx * edgeDx) + (edgeDy * edgeDy));
        if (edgeLength < 0.000001)
            return candidates;

        var baseT = LeaderAnchorResolver.ClampAwayFromCorners(hit.T, edgeLength);
        var shiftDistance = Math.Min(Math.Max(depthMm, 1.0), edgeLength * 0.25);
        var shiftT = edgeLength < 0.000001 ? 0.0 : shiftDistance / edgeLength;
        AddCandidate(candidates, polygon, snapshot, hit, hitIndex: hit.EdgeIndex, LeaderAnchorCandidateKind.Nearest, baseT, depthMm, minFarEdgeClearanceMm);
        AddCandidate(candidates, polygon, snapshot, hit, hitIndex: hit.EdgeIndex, LeaderAnchorCandidateKind.ShiftedLeft, baseT - shiftT, depthMm, minFarEdgeClearanceMm);
        AddCandidate(candidates, polygon, snapshot, hit, hitIndex: hit.EdgeIndex, LeaderAnchorCandidateKind.ShiftedRight, baseT + shiftT, depthMm, minFarEdgeClearanceMm);
        return candidates;
    }

    private static void AddCandidate(
        List<LeaderAnchorCandidate> candidates,
        IReadOnlyList<double[]> polygon,
        LeaderSnapshot snapshot,
        LeaderAnchorResolver.EdgeHit hit,
        int hitIndex,
        LeaderAnchorCandidateKind kind,
        double edgeT,
        double depthMm,
        double minFarEdgeClearanceMm)
    {
        if (!LeaderAnchorResolver.TryResolveAnchorTargetFromEdgePoint(
                polygon,
                hit.StartX,
                hit.StartY,
                hit.EndX,
                hit.EndY,
                edgeT,
                depthMm,
                minFarEdgeClearanceMm,
                out var edgePointX,
                out var edgePointY,
                out var anchorX,
                out var anchorY,
                out var cornerDistance,
                out var farEdgeClearance))
        {
            return;
        }

        var referencePoint = snapshot.LeaderEndPoint ?? snapshot.InsertionPoint ?? snapshot.AnchorPoint;
        var lineLength = 0.0;
        if (referencePoint != null)
        {
            var dx = referencePoint.X - anchorX;
            var dy = referencePoint.Y - anchorY;
            lineLength = Math.Sqrt((dx * dx) + (dy * dy));
        }

        candidates.Add(new LeaderAnchorCandidate
        {
            Kind = kind,
            EdgeIndex = hitIndex,
            EdgeT = LeaderAnchorResolver.ClampAwayFromCorners(
                edgeT,
                Math.Sqrt(Math.Pow(hit.EndX - hit.StartX, 2) + Math.Pow(hit.EndY - hit.StartY, 2))),
            EdgePoint = new DrawingPointInfo { X = edgePointX, Y = edgePointY },
            AnchorPoint = new DrawingPointInfo { X = anchorX, Y = anchorY },
            CornerDistance = cornerDistance,
            FarEdgeClearance = farEdgeClearance,
            LineLengthToLeaderEnd = lineLength,
        });
    }
}
