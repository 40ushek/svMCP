using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
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
}
