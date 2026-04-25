using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class LeaderTextOverlapAnalyzerTests
{
    [Fact]
    public void Analyze_DetectsForeignTextCrossing()
    {
        var marks = new[]
        {
            CreateMark(1, CreateRectangle(-100, -100, -90, -90), [[-10, 5], [30, 5]]),
            CreateMark(2, CreateRectangle(20, 0, 30, 10), []),
        };

        var result = LeaderTextOverlapAnalyzer.Analyze(marks, ownEndIgnoreDistance: 1.0);

        Assert.Equal(1, result.TotalCrossings);
        Assert.Equal(0, result.OwnCrossings);
        Assert.Equal(1, result.ForeignCrossings);
        Assert.Contains(result.Conflicts, conflict => conflict.MarkId == 1 && conflict.CrossedMarkId == 2 && !conflict.IsOwn);
    }

    [Fact]
    public void Analyze_DetectsOwnTextCrossingAwayFromLeaderEnd()
    {
        var marks = new[]
        {
            CreateMark(1, CreateRectangle(0, 0, 10, 10), [[5, -10], [5, 20], [20, 20]]),
        };

        var result = LeaderTextOverlapAnalyzer.Analyze(marks, ownEndIgnoreDistance: 1.0);

        Assert.Equal(1, result.TotalCrossings);
        Assert.Equal(1, result.OwnCrossings);
        Assert.Equal(0, result.ForeignCrossings);
        Assert.Equal(0, result.Conflicts[0].SegmentIndex);
    }

    [Fact]
    public void Analyze_IgnoresOwnShortTouchNearLeaderEnd()
    {
        var marks = new[]
        {
            CreateMark(1, CreateRectangle(0, 0, 10, 10), [[-10, 5], [0.5, 5]]),
        };

        var result = LeaderTextOverlapAnalyzer.Analyze(marks, ownEndIgnoreDistance: 1.0);

        Assert.Equal(0, result.TotalCrossings);
    }

    [Fact]
    public void Analyze_ReturnsNoConflictForSeparatedLeader()
    {
        var marks = new[]
        {
            CreateMark(1, CreateRectangle(0, 0, 10, 10), [[-10, -10], [-5, -10]]),
            CreateMark(2, CreateRectangle(20, 0, 30, 10), []),
        };

        var result = LeaderTextOverlapAnalyzer.Analyze(marks, ownEndIgnoreDistance: 1.0);

        Assert.Equal(0, result.TotalCrossings);
    }

    [Fact]
    public void Analyze_ChecksAllPolylineSegments()
    {
        var marks = new[]
        {
            CreateMark(1, CreateRectangle(0, 0, 10, 10), [[-10, -10], [25, -10], [25, 20]]),
            CreateMark(2, CreateRectangle(20, 0, 30, 10), []),
        };

        var result = LeaderTextOverlapAnalyzer.Analyze(marks, ownEndIgnoreDistance: 1.0);

        Assert.Equal(1, result.TotalCrossings);
        Assert.Equal(0, result.OwnCrossings);
        Assert.Equal(1, result.ForeignCrossings);
        Assert.Equal(1, result.Conflicts[0].SegmentIndex);
    }

    private static LeaderTextOverlapMark CreateMark(
        int id,
        List<double[]> textPolygon,
        List<double[]> leaderPolyline) => new()
    {
        MarkId = id,
        TextPolygon = textPolygon,
        LeaderPolyline = leaderPolyline
    };

    private static List<double[]> CreateRectangle(double minX, double minY, double maxX, double maxY) =>
    [
        [minX, minY],
        [maxX, minY],
        [maxX, maxY],
        [minX, maxY],
    ];
}
