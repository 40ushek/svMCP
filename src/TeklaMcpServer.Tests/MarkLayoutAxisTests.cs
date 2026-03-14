using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkLayoutAxisTests
{
    [Fact]
    public void GenerateCandidates_ForAxisBoundMark_KeepsCandidatesOnAxis()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 200,
            Width = 40,
            Height = 20,
            HasAxis = true,
            AxisDx = 0,
            AxisDy = 1
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions());

        Assert.All(candidates, candidate => Assert.Equal(100, candidate.X, 6));
        Assert.Contains(candidates, candidate => candidate.Y > 200);
        Assert.Contains(candidates, candidate => candidate.Y < 200);
    }

    [Fact]
    public void Resolve_ForParallelAxisMarks_MovesOnlyAlongAxis()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1,
                X = 100,
                Y = 100,
                Width = 40,
                Height = 80,
                HasAxis = true,
                AxisDx = 0,
                AxisDy = 1,
                CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 2,
                X = 100,
                Y = 130,
                Width = 40,
                Height = 80,
                HasAxis = true,
                AxisDx = 0,
                AxisDy = -1,
                CanMove = true
            }
        };

        var resolved = resolver.Resolve(placements, new MarkLayoutOptions { Gap = 2.0 }, out _);
        var a = resolved.Single(x => x.Id == 1);
        var b = resolved.Single(x => x.Id == 2);

        Assert.Equal(100, a.X, 6);
        Assert.Equal(100, b.X, 6);
        Assert.True(a.Y < 100);
        Assert.True(b.Y > 130);
        Assert.Equal(0, resolver.CountOverlaps(resolved));
    }

    [Fact]
    public void Resolve_ForUnnormalizedAxisMarks_NormalizesAxesBeforeResolving()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1,
                X = 100,
                Y = 100,
                Width = 40,
                Height = 80,
                HasAxis = true,
                AxisDx = 0,
                AxisDy = 10,
                CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 2,
                X = 100,
                Y = 130,
                Width = 40,
                Height = 80,
                HasAxis = true,
                AxisDx = 0,
                AxisDy = -25,
                CanMove = true
            }
        };

        var resolved = resolver.Resolve(placements, new MarkLayoutOptions { Gap = 2.0 }, out _);
        var a = resolved.Single(x => x.Id == 1);
        var b = resolved.Single(x => x.Id == 2);

        Assert.Equal(100, a.X, 6);
        Assert.Equal(100, b.X, 6);
        Assert.True(a.Y < 100);
        Assert.True(b.Y > 130);
        Assert.Equal(0, resolver.CountOverlaps(resolved));
    }
}
