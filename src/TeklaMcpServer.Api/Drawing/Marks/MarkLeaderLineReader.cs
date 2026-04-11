using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkLeaderLineReader
{
    public static List<LeaderLineSnapshot> ReadSnapshots(Mark mark)
    {
        if (mark == null)
            throw new ArgumentNullException(nameof(mark));

        var result = new List<LeaderLineSnapshot>();
        var children = mark.GetObjects();
        while (children.MoveNext())
        {
            if (children.Current is not LeaderLine leaderLine)
                continue;

            var snapshot = new LeaderLineSnapshot
            {
                Type = leaderLine.LeaderLineType.ToString(),
                StartPoint = CreatePoint(leaderLine.StartPoint.X, leaderLine.StartPoint.Y),
                EndPoint = CreatePoint(leaderLine.EndPoint.X, leaderLine.EndPoint.Y),
            };

            var order = 0;
            foreach (Point elbowPoint in leaderLine.ElbowPoints)
            {
                snapshot.ElbowPoints.Add(CreatePoint(elbowPoint.X, elbowPoint.Y, order));
                order++;
            }

            result.Add(snapshot);
        }

        return result;
    }

    public static List<MarkLeaderLineInfo> CreateInfos(Mark mark) =>
        ToInfos(ReadSnapshots(mark));

    public static List<MarkLeaderLineInfo> ToInfos(IReadOnlyList<LeaderLineSnapshot> snapshots)
    {
        if (snapshots == null)
            throw new ArgumentNullException(nameof(snapshots));

        return snapshots
            .Select(static snapshot => new MarkLeaderLineInfo
            {
                Type = snapshot.Type,
                StartX = Math.Round(snapshot.StartPoint?.X ?? 0.0, 2),
                StartY = Math.Round(snapshot.StartPoint?.Y ?? 0.0, 2),
                EndX = Math.Round(snapshot.EndPoint?.X ?? 0.0, 2),
                EndY = Math.Round(snapshot.EndPoint?.Y ?? 0.0, 2),
                ElbowPoints = snapshot.ElbowPoints
                    .Select(static point => new[]
                    {
                        Math.Round(point.X, 2),
                        Math.Round(point.Y, 2),
                    })
                    .ToList()
            })
            .ToList();
    }

    private static DrawingPointInfo CreatePoint(double x, double y, int order = -1) => new()
    {
        X = x,
        Y = y,
        Order = order,
    };
}
