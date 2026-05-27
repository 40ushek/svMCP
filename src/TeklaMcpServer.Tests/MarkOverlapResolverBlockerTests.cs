using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkOverlapResolverBlockerTests
{
    [Fact]
    public void Resolve_DoesNotPushMarkIntoFixedBlocker()
    {
        // Two marks overlap each other. Blocker is to the right.
        // Mark B would normally be pushed right — blocker should prevent that.
        var a = new MarkLayoutPlacement
        {
            Id = 1,
            X = 0, Y = 0,
            Width = 20, Height = 10,
            AnchorX = 0, AnchorY = 0,
            CanMove = false,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        var b = new MarkLayoutPlacement
        {
            Id = 2,
            X = 5, Y = 0,
            Width = 20, Height = 10,
            AnchorX = 5, AnchorY = 0,
            CanMove = true,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        // Blocker to the right: x=[20,60], y=[-10,10]
        var blocker = new List<double[]>
        {
            new[] { 20.0, -10.0 },
            new[] { 60.0, -10.0 },
            new[] { 60.0,  10.0 },
            new[] { 20.0,  10.0 }
        };

        var options = new MarkLayoutOptions
        {
            Gap = 1.0,
            FixedTextBoxPolygons = [blocker],
            MaxResolverIterations = 5
        };

        var resolver = new MarkOverlapResolver();
        var result = resolver.Resolve([a, b], options, out _);

        var resolvedB = result[1];

        // B must not have been pushed into the blocker (x+10 < 20)
        Assert.True(resolvedB.X + 10.0 <= 20.0 + 0.01,
            $"Mark B was pushed into blocker: X={resolvedB.X}");
    }

    [Fact]
    public void ResolvePlacedMarks_DoesNotNudgeMarkIntoFixedBlocker()
    {
        var a = new MarkLayoutPlacement
        {
            Id = 1,
            X = 0, Y = 0,
            Width = 20, Height = 10,
            AnchorX = 0, AnchorY = 0,
            CanMove = false,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        var b = new MarkLayoutPlacement
        {
            Id = 2,
            X = 5, Y = 0,
            Width = 20, Height = 10,
            AnchorX = 5, AnchorY = 0,
            CanMove = true,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        // Blocker to the right
        var blocker = new List<double[]>
        {
            new[] { 20.0, -10.0 },
            new[] { 60.0, -10.0 },
            new[] { 60.0,  10.0 },
            new[] { 20.0,  10.0 }
        };

        var options = new MarkLayoutOptions
        {
            Gap = 1.0,
            FixedTextBoxPolygons = [blocker],
            MaxResolverIterations = 5
        };

        var resolver = new MarkOverlapResolver();
        var result = resolver.ResolvePlacedMarks([a, b], options, out _);

        var resolvedB = result[1];

        Assert.True(resolvedB.X + 10.0 <= 20.0 + 0.01,
            $"Mark B was nudged into blocker: X={resolvedB.X}");
    }

    [Fact]
    public void Resolve_AxisMark_DoesNotPushIntoFixedBlocker_ViaTryResolveAlongAxis()
    {
        // Two axis-bound marks overlapping along X axis.
        // Blocker to the right — TryResolveAlongAxis must respect it.
        var a = new MarkLayoutPlacement
        {
            Id = 1,
            X = 0, Y = 0,
            Width = 20, Height = 10,
            AnchorX = 0, AnchorY = 0,
            CanMove = false,
            HasAxis = true,
            AxisDx = 1, AxisDy = 0,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        var b = new MarkLayoutPlacement
        {
            Id = 2,
            X = 5, Y = 0,
            Width = 20, Height = 10,
            AnchorX = 5, AnchorY = 0,
            CanMove = true,
            HasAxis = true,
            AxisDx = 1, AxisDy = 0,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        // Blocker immediately to the right: x=[20,60], y=[-10,10]
        var blocker = new List<double[]>
        {
            new[] { 20.0, -10.0 },
            new[] { 60.0, -10.0 },
            new[] { 60.0,  10.0 },
            new[] { 20.0,  10.0 }
        };

        var options = new MarkLayoutOptions
        {
            Gap = 1.0,
            FixedTextBoxPolygons = [blocker],
            MaxResolverIterations = 5
        };

        var resolver = new MarkOverlapResolver();
        var result = resolver.Resolve([a, b], options, out _);

        var resolvedB = result[1];

        Assert.True(resolvedB.X + 10.0 <= 20.0 + 0.01,
            $"Axis mark B was pushed into blocker via TryResolveAlongAxis: X={resolvedB.X}");
    }
}
