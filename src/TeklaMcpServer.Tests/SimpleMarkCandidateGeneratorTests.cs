using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class SimpleMarkCandidateGeneratorTests
{
    [Fact]
    public void GenerateCandidates_LocalMark_UsesCenterOfSourcePartAsRingOrigin()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 50,
            CurrentY = 50,
            AnchorX = 50,
            AnchorY = 50,
            Width = 20,
            Height = 10,
            SourceCenterX = 100,
            SourceCenterY = 200,
            CanMove = true
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            MaxDistanceFromAnchor = 0
        });

        // With MaxDistanceFromAnchor=0 (disabled), all candidates pass the filter.
        // The ring should be centred on SourceCenter (100,200), not CurrentX/Y (50,50).
        var nonCurrent = candidates.Where(c => !(c.X == 50 && c.Y == 50)).ToList();
        Assert.True(nonCurrent.Count > 0);
        var avgX = nonCurrent.Average(c => c.X);
        var avgY = nonCurrent.Average(c => c.Y);
        Assert.Equal(100, avgX, 1);
        Assert.Equal(200, avgY, 1);
    }

    [Fact]
    public void GenerateCandidates_LocalMark_WithoutSourceCenter_UsesCurrentPositionAsRingOrigin()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 50,
            CurrentY = 50,
            AnchorX = 50,
            AnchorY = 50,
            Width = 20,
            Height = 10,
            CanMove = true
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            MaxDistanceFromAnchor = 0
        });

        var nonCurrent = candidates.Where(c => !(c.X == 50 && c.Y == 50)).ToList();
        Assert.True(nonCurrent.Count > 0);
        var avgX = nonCurrent.Average(c => c.X);
        var avgY = nonCurrent.Average(c => c.Y);
        Assert.Equal(50, avgX, 1);
        Assert.Equal(50, avgY, 1);
    }

    [Fact]
    public void GenerateCandidates_LocalMark_AnchorDistanceFilterUsesSourceCenter()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 50,
            CurrentY = 50,
            AnchorX = 50,
            AnchorY = 50,
            Width = 20,
            Height = 10,
            SourceCenterX = 100,
            SourceCenterY = 200,
            CanMove = true
        };

        // MaxDistanceFromAnchor=5 from source center (100,200):
        // current position (50,50) is ~158mm away → filtered out
        // ring candidates near (100,200) within 5mm offset → pass
        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            MaxDistanceFromAnchor = 20,
            CandidateOffset = 0,
            Gap = 0,
            CandidateDistanceMultipliers = new[] { 1.0 }
        });

        // All non-current candidates should be near source center, not near current (50,50)
        var nonCurrent = candidates.Where(c => !(c.X == 50 && c.Y == 50)).ToList();
        Assert.True(nonCurrent.Count > 0);
        Assert.All(nonCurrent, c =>
        {
            var dx = c.X - 100;
            var dy = c.Y - 200;
            Assert.True(System.Math.Sqrt(dx * dx + dy * dy) <= 20,
                $"Candidate ({c.X},{c.Y}) is too far from source center (100,200)");
        });
    }

    [Fact]
    public void GenerateCandidates_LeaderMarkWithHorizontalSource_GeneratesTopBottomMidAndShiftedVariants()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 140,
            CurrentY = 80,
            AnchorX = 50,
            AnchorY = 10,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 42,
            CanMove = true
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            Gap = 2,
            CandidateOffset = 4,
            PartPolygonsByModelId = new()
            {
                [42] =
                [
                    [0.0, 0.0],
                    [100.0, 0.0],
                    [100.0, 20.0],
                    [0.0, 20.0]
                ]
            }
        });

        Assert.Contains(candidates, c => c.Priority == 0 && c.X == 140 && c.Y == 80);
        Assert.Contains(candidates, c => c.X == 50 && c.Y == 31);
        Assert.Contains(candidates, c => c.X == 50 && c.Y == -11);
        Assert.Contains(candidates, c => c.X == 72 && c.Y == 31);
        Assert.Contains(candidates, c => c.X == 28 && c.Y == 31);
        Assert.Contains(candidates, c => c.X == 72 && c.Y == -11);
        Assert.Contains(candidates, c => c.X == 28 && c.Y == -11);
    }

    [Fact]
    public void GenerateCandidates_LeaderMarkWithVerticalSource_GeneratesLeftRightMidAndShiftedVariants()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 90,
            CurrentY = 140,
            AnchorX = 10,
            AnchorY = 50,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 7,
            CanMove = true
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            Gap = 2,
            CandidateOffset = 4,
            PartPolygonsByModelId = new()
            {
                [7] =
                [
                    [0.0, 0.0],
                    [20.0, 0.0],
                    [20.0, 100.0],
                    [0.0, 100.0]
                ]
            }
        });

        Assert.Contains(candidates, c => c.X == -16 && c.Y == 50);
        Assert.Contains(candidates, c => c.X == 36 && c.Y == 50);
        Assert.Contains(candidates, c => c.X == -16 && c.Y == 62);
        Assert.Contains(candidates, c => c.X == -16 && c.Y == 38);
        Assert.Contains(candidates, c => c.X == 36 && c.Y == 62);
        Assert.Contains(candidates, c => c.X == 36 && c.Y == 38);
    }

    [Fact]
    public void GenerateCandidates_LeaderMarkWithCompactSource_GeneratesFourSideMidCandidates()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 70,
            CurrentY = 70,
            AnchorX = 30,
            AnchorY = 30,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 8,
            CanMove = true
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            Gap = 2,
            CandidateOffset = 4,
            PartPolygonsByModelId = new()
            {
                [8] =
                [
                    [0.0, 0.0],
                    [40.0, 0.0],
                    [40.0, 40.0],
                    [0.0, 40.0]
                ]
            }
        });

        Assert.Contains(candidates, c => c.X == 20 && c.Y == 51);
        Assert.Contains(candidates, c => c.X == 20 && c.Y == -11);
        Assert.Contains(candidates, c => c.X == -16 && c.Y == 20);
        Assert.Contains(candidates, c => c.X == 56 && c.Y == 20);
    }

    [Fact]
    public void GenerateCandidates_LeaderMarkWithoutSourcePolygon_FallsBackToRingCandidates()
    {
        var generator = new SimpleMarkCandidateGenerator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 50,
            AnchorY = 50,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 123,
            CanMove = true
        };

        var candidates = generator.GenerateCandidates(item, new MarkLayoutOptions
        {
            Gap = 0,
            CandidateOffset = 0,
            CandidateDistanceMultipliers = new[] { 1.0 }
        });

        Assert.Contains(candidates, c => c.X == 100 && c.Y == 100);
        Assert.Contains(candidates, c => c.X == 60 && c.Y == 55);
        Assert.Contains(candidates, c => c.X == 40 && c.Y == 45);
    }
}
