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

    [Fact]
    public void Resolve_ForOrthogonalAxisMarks_MovesEachAlongOwnAxis()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1,
                X = 100,
                Y = 100,
                Width = 120,
                Height = 40,
                HasAxis = true,
                AxisDx = 1,
                AxisDy = 0,
                CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 2,
                X = 120,
                Y = 120,
                Width = 40,
                Height = 120,
                HasAxis = true,
                AxisDx = 0,
                AxisDy = 1,
                CanMove = true
            }
        };

        var resolved = resolver.Resolve(placements, new MarkLayoutOptions { Gap = 2.0 }, out _);
        var a = resolved.Single(x => x.Id == 1);
        var b = resolved.Single(x => x.Id == 2);

        Assert.Equal(100, a.Y, 6);
        Assert.Equal(120, b.X, 6);
        Assert.NotEqual(100, a.X);
        Assert.Equal(0, resolver.CountOverlaps(resolved));
    }

    [Fact]
    public void CountOverlaps_ForPerpendicularFramesWithoutPolygonIntersection_ReturnsZero()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1,
                X = 1452.8,
                Y = 4211.03,
                Width = 111.0,
                Height = 383.32,
                LocalCorners = new()
                {
                    new[] { -55.5, -191.66 },
                    new[] { -55.5, 191.66 },
                    new[] { 55.5, 191.66 },
                    new[] { 55.5, -191.66 }
                }
            },
            new MarkLayoutPlacement
            {
                Id = 2,
                X = 1407.8,
                Y = 3833.4,
                Width = 383.32,
                Height = 111.0,
                LocalCorners = new()
                {
                    new[] { -191.66, -55.5 },
                    new[] { 191.66, -55.5 },
                    new[] { 191.66, 55.5 },
                    new[] { -191.66, 55.5 }
                }
            }
        };

        Assert.Equal(0, resolver.CountOverlaps(placements));
    }

    [Fact]
    public void ResolvePlacedMarks_MovesOnlyConflictingComponent()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1, X = 0, Y = 0, Width = 80, Height = 40, AnchorX = 0, AnchorY = 0, CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 2, X = 20, Y = 0, Width = 80, Height = 40, AnchorX = 20, AnchorY = 0, CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 3, X = 300, Y = 300, Width = 80, Height = 40, AnchorX = 300, AnchorY = 300, CanMove = true
            }
        };

        var resolved = resolver.ResolvePlacedMarks(
            placements,
            new MarkLayoutOptions { Gap = 2.0, MaxResolverIterations = 20 },
            out _);

        var first = resolved.Single(x => x.Id == 1);
        var second = resolved.Single(x => x.Id == 2);
        var third = resolved.Single(x => x.Id == 3);

        Assert.Equal(0, resolver.CountOverlaps(resolved));
        Assert.True(
            System.Math.Abs(first.X) > 0.01 ||
            System.Math.Abs(first.Y) > 0.01);
        Assert.True(
            System.Math.Abs(second.X - 20) > 0.01 ||
            System.Math.Abs(second.Y) > 0.01);
        Assert.Equal(300, third.X, 6);
        Assert.Equal(300, third.Y, 6);
    }

    [Fact]
    public void ResolvePlacedMarks_RespectsMaxDistanceFromAnchor()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1, X = 0, Y = 0, Width = 120, Height = 40, AnchorX = 0, AnchorY = 0, CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 2, X = 10, Y = 0, Width = 120, Height = 40, AnchorX = 10, AnchorY = 0, CanMove = true
            }
        };

        var resolved = resolver.ResolvePlacedMarks(
            placements,
            new MarkLayoutOptions
            {
                Gap = 2.0,
                MaxResolverIterations = 20,
                MaxDistanceFromAnchor = 18.0
            },
            out _);

        foreach (var mark in resolved)
        {
            var dx = mark.X - mark.AnchorX;
            var dy = mark.Y - mark.AnchorY;
            var distance = System.Math.Sqrt((dx * dx) + (dy * dy));
            Assert.True(distance <= 18.001);
        }
    }

    [Fact]
    public void ResolvePlacedMarks_ForLeaderAndBaselineConflict_PrefersLeaderMovement()
    {
        var resolver = new MarkOverlapResolver();
        var placements = new[]
        {
            new MarkLayoutPlacement
            {
                Id = 1,
                X = 0,
                Y = 0,
                Width = 40,
                Height = 20,
                AnchorX = 0,
                AnchorY = 0,
                HasLeaderLine = true,
                CanMove = true
            },
            new MarkLayoutPlacement
            {
                Id = 2,
                X = 10,
                Y = 0,
                Width = 40,
                Height = 20,
                AnchorX = 10,
                AnchorY = 0,
                HasLeaderLine = false,
                HasAxis = true,
                AxisDx = 0,
                AxisDy = 1,
                CanMove = true
            }
        };

        var resolved = resolver.ResolvePlacedMarks(
            placements,
            new MarkLayoutOptions
            {
                Gap = 2.0,
                MaxResolverIterations = 24,
                MaxDistanceFromAnchor = 40.0
            },
            out _);

        var leader = resolved.Single(x => x.Id == 1);
        var baseline = resolved.Single(x => x.Id == 2);

        var leaderDistance = System.Math.Sqrt(
            ((leader.X - 0) * (leader.X - 0)) +
            ((leader.Y - 0) * (leader.Y - 0)));
        var baselineDistance = System.Math.Sqrt(
            ((baseline.X - 10) * (baseline.X - 10)) +
            ((baseline.Y - 0) * (baseline.Y - 0)));

        Assert.True(leaderDistance >= baselineDistance);
        Assert.Equal(10, baseline.X, 6);
    }
}
