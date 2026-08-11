using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class SimpleMarkCostEvaluatorTests
{
    [Fact]
    public void EvaluateCandidate_PrefersCandidateCloserToSourceCenter_ForNonLeaderMark()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 100,
            AnchorY = 100,
            Width = 20,
            Height = 10,
            SourceCenterX = 200,
            SourceCenterY = 100,
            CanMove = true
        };
        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 1.0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0
        };

        var near = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 180, Y = 100 }, new List<MarkLayoutPlacement>(), options);
        var far = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 120, Y = 100 }, new List<MarkLayoutPlacement>(), options);

        Assert.True(near < far);
    }

    [Fact]
    public void EvaluateCandidate_IgnoresSourceCenter_ForLeaderLineMark()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 100,
            AnchorY = 100,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            SourceCenterX = 1000,
            SourceCenterY = 1000,
            CanMove = true
        };
        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 10.0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0
        };

        var left = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 80, Y = 100 }, new List<MarkLayoutPlacement>(), options);
        var right = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 120, Y = 100 }, new List<MarkLayoutPlacement>(), options);

        Assert.Equal(left, right, 6);
    }

    [Fact]
    public void EvaluateCandidate_PrefersCandidateInsideOwnPartGeometry_ForNonLeaderPartMark()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 100,
            AnchorY = 100,
            Width = 20,
            Height = 10,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 42,
            SourceCenterX = 100,
            SourceCenterY = 100,
            CanMove = true
        };
        var viewContext = new DrawingViewContext();
        viewContext.Parts.Add(new PartInView
        {
            Success = true,
            ModelId = 42,
            BboxMin = [80.0, 80.0],
            BboxMax = [120.0, 120.0]
        });

        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 0,
            SourceOutsideOwnPartPenalty = 100.0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0,
            ViewContext = viewContext,
            PartPolygonsByModelId = MarkSourceResolver.BuildPartPolygons(viewContext.Parts)
        };

        var inside = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 100, Y = 100 }, new List<MarkLayoutPlacement>(), options);
        var outside = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 140, Y = 100 }, new List<MarkLayoutPlacement>(), options);

        Assert.True(inside < outside);
    }

    [Fact]
    public void EvaluateCandidate_PenalizesCandidateOverlappingForeignPart()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 100,
            AnchorY = 100,
            Width = 20,
            Height = 10,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 42,
            SourceCenterX = 100,
            SourceCenterY = 100,
            CanMove = true,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] { 10.0, -5.0 },
                new[] { 10.0, 5.0 },
                new[] { -10.0, 5.0 }
            }
        };
        var viewContext = new DrawingViewContext();
        viewContext.Parts.Add(new PartInView
        {
            Success = true,
            ModelId = 42,
            BboxMin = [80.0, 80.0],
            BboxMax = [120.0, 120.0]
        });
        viewContext.Parts.Add(new PartInView
        {
            Success = true,
            ModelId = 99,
            BboxMin = [140.0, 90.0],
            BboxMax = [180.0, 110.0]
        });

        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 0,
            SourceOutsideOwnPartPenalty = 0,
            ForeignPartOverlapPenalty = 100.0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0,
            ViewContext = viewContext,
            PartPolygonsByModelId = MarkSourceResolver.BuildPartPolygons(viewContext.Parts)
        };

        var clear = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 100, Y = 100 }, new List<MarkLayoutPlacement>(), options);
        var overlappingForeign = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 150, Y = 100 }, new List<MarkLayoutPlacement>(), options);

        Assert.True(clear < overlappingForeign);
    }

    [Fact]
    public void EvaluateCandidate_PenalizesCandidateWhoseLeaderCrossesAnotherLeader()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 0,
            AnchorY = 0,
            Width = 10,
            Height = 10,
            HasLeaderLine = true,
            CanMove = true
        };

        // Placed mark: body at (0,100), anchor at (100,0) — its leader goes from (0,100)→(100,0)
        var crossingPlacement = new MarkLayoutPlacement
        {
            Id = 2,
            X = 0,
            Y = 100,
            Width = 10,
            Height = 10,
            AnchorX = 100,
            AnchorY = 0,
            HasLeaderLine = true,
            CanMove = false
        };

        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0,
            LeaderCrossingPenalty = 500.0
        };

        // Candidate at (100,0): leader goes (100,0)→(0,0) — does NOT cross (0,100)→(100,0)
        var noCross = evaluator.EvaluateCandidate(
            item,
            new MarkCandidate { X = 100, Y = 0 },
            new List<MarkLayoutPlacement> { crossingPlacement },
            options);

        // Candidate at (100,100): leader goes (100,100)→(0,0) — CROSSES (0,100)→(100,0)
        var withCross = evaluator.EvaluateCandidate(
            item,
            new MarkCandidate { X = 100, Y = 100 },
            new List<MarkLayoutPlacement> { crossingPlacement },
            options);

        Assert.True(noCross < withCross);
        Assert.Equal(500.0, withCross - noCross, 6);
    }

    [Fact]
    public void EvaluateCandidate_PenalizesCandidateOverlappingFixedBlocker()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 0,
            CurrentY = 0,
            AnchorX = 0,
            AnchorY = 0,
            Width = 20,
            Height = 10,
            CanMove = true,
            LocalCorners =
            {
                new[] { -10.0, -5.0 },
                new[] {  10.0, -5.0 },
                new[] {  10.0,  5.0 },
                new[] { -10.0,  5.0 }
            }
        };

        // Blocker at x=[80,120], y=[45,55]
        var blockerPolygon = new List<double[]>
        {
            new[] { 80.0,  45.0 },
            new[] { 120.0, 45.0 },
            new[] { 120.0, 55.0 },
            new[] { 80.0,  55.0 }
        };

        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 1_000_000.0,
            FixedTextBoxPolygons = [blockerPolygon]
        };

        // Candidate at (100, 50) — mark polygon [90,45]..[110,55] overlaps blocker
        var overlapping = evaluator.EvaluateCandidate(
            item, new MarkCandidate { X = 100, Y = 50 }, new List<MarkLayoutPlacement>(), options);

        // Candidate at (0, 0) — mark polygon [-10,-5]..[10,5] — no overlap
        var clear = evaluator.EvaluateCandidate(
            item, new MarkCandidate { X = 0, Y = 0 }, new List<MarkLayoutPlacement>(), options);

        Assert.True(clear < overlapping);
        Assert.Equal(1_000_000.0, overlapping - clear, 1);
    }

    [Fact]
    public void EvaluateCandidate_PenalizesCandidateOverlappingFixedBlocker_WithoutLocalCorners()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 0,
            CurrentY = 0,
            AnchorX = 0,
            AnchorY = 0,
            Width = 20,
            Height = 10,
            CanMove = true
            // LocalCorners intentionally empty — AABB fallback path
        };

        var blockerPolygon = new List<double[]>
        {
            new[] { 80.0, 45.0 },
            new[] { 120.0, 45.0 },
            new[] { 120.0, 55.0 },
            new[] { 80.0,  55.0 }
        };

        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 1_000_000.0,
            FixedTextBoxPolygons = [blockerPolygon]
        };

        // Candidate at (100, 50) — AABB [90,45]..[110,55] overlaps blocker
        var overlapping = evaluator.EvaluateCandidate(
            item, new MarkCandidate { X = 100, Y = 50 }, new List<MarkLayoutPlacement>(), options);

        // Candidate at (0, 0) — AABB [-10,-5]..[10,5] — no overlap
        var clear = evaluator.EvaluateCandidate(
            item, new MarkCandidate { X = 0, Y = 0 }, new List<MarkLayoutPlacement>(), options);

        Assert.True(clear < overlapping);
        Assert.Equal(1_000_000.0, overlapping - clear, 1);
    }
}
