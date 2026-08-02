using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.Dimensions.Defects;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaDrawingDimensionDefectApiTests
{
    [Fact]
    public void ReadsEachLiveSourceOnceAndBuildsCoverageForEveryEligibleChain()
    {
        var contextReads = 0;
        var partReads = 0;
        var candidateReads = 0;
        var contexts = new GetDimensionContextsResult
        {
            ViewId = 12,
            Dimensions =
            [
                Chain(101, (0, 0), (50, 0), (100, 0)),
                Chain(102, (0, 10), (50, 10), (100, 10))
            ]
        };
        var parts = new List<PartGeometryInViewResult> { Part(7, solidComplete: true) };

        var api = new TeklaDrawingDimensionDefectApi(
            _ =>
            {
                contextReads++;
                return contexts;
            },
            _ =>
            {
                partReads++;
                return parts;
            },
            (_, modelId) =>
            {
                candidateReads++;
                return CandidateResult(modelId, solidComplete: true,
                    (0, 0, DrawingPartCandidateConfidence.ExactGeometry),
                    (50, 0, DrawingPartCandidateConfidence.ExactGeometry),
                    (100, 0, DrawingPartCandidateConfidence.ExactGeometry),
                    (0, 10, DrawingPartCandidateConfidence.ExactGeometry),
                    (50, 10, DrawingPartCandidateConfidence.ExactGeometry),
                    (100, 10, DrawingPartCandidateConfidence.ExactGeometry));
            });

        var report = api.GetDimensionDefects(12);

        Assert.Equal(1, contextReads);
        Assert.Equal(1, partReads);
        Assert.Equal(1, candidateReads);
        Assert.Equal(2, report.Chains.Count);
        Assert.DoesNotContain(report.Warnings, static warning => warning.StartsWith("no coverage for chain"));
    }

    [Fact]
    public void CandidateReadFailureDisablesAnchorChecksInsteadOfInventingMissingPoints()
    {
        var contexts = new GetDimensionContextsResult
        {
            ViewId = 12,
            Dimensions = [Chain(201, (0, 0), (50, 0), (100, 0))]
        };

        var api = new TeklaDrawingDimensionDefectApi(
            _ => contexts,
            _ => [Part(7, solidComplete: true)],
            (_, modelId) => new GetPartCandidatePointsResult
            {
                Success = false,
                ModelId = modelId,
                Error = "solid traversal failed"
            });

        var report = api.GetDimensionDefects(12);

        Assert.DoesNotContain(report.Defects, static defect =>
            defect.Kind is DimensionDefectKind.UnanchoredPoint
                or DimensionDefectKind.PhantomAnchor
                or DimensionDefectKind.AnchorUnverified
                or DimensionDefectKind.AmbiguousAnchor);
        Assert.Contains(report.Warnings, static warning => warning.StartsWith("candidates_unavailable:7"));
        Assert.Contains(report.Warnings, static warning => warning == "no coverage for chain 201; its anchors were not checked");
    }

    [Fact]
    public void CandidateSolidCompletenessControlsFallbackConfidence()
    {
        var contexts = new GetDimensionContextsResult
        {
            ViewId = 12,
            Dimensions = [Chain(301, (0, 0), (50, 0), (100, 0))]
        };
        var part = Part(7, solidComplete: true);
        var api = new TeklaDrawingDimensionDefectApi(
            _ => contexts,
            _ => [part],
            (_, modelId) => CandidateResult(modelId, solidComplete: false,
                (0, 0, DrawingPartCandidateConfidence.BoundingBoxFallback),
                (50, 0, DrawingPartCandidateConfidence.BoundingBoxFallback),
                (100, 0, DrawingPartCandidateConfidence.BoundingBoxFallback)));

        var report = api.GetDimensionDefects(12);

        Assert.False(part.SolidGeometryComplete);
        Assert.Contains(report.Defects, static defect =>
            defect.Kind == DimensionDefectKind.AnchorUnverified &&
            defect.Confidence == DimensionDefectConfidence.Provisional);
        Assert.DoesNotContain(report.Defects, static defect => defect.Kind == DimensionDefectKind.PhantomAnchor);
    }

    [Fact]
    public void CandidateReadIsAuthoritativeForSolidCompletenessEvenWhenTheGeometryReadWasIncomplete()
    {
        // The reverse of the case above: the separate geometry read came back incomplete, but the
        // read that actually produced the candidates was complete. Anchor confidence must follow
        // the read that produced the evidence being judged, not be dragged down by an unrelated
        // read that happened to be worse. ANDing the two flags would wrongly downgrade a real
        // PhantomAnchor to the weaker, human-review-only AnchorUnverified.
        var contexts = new GetDimensionContextsResult
        {
            ViewId = 12,
            Dimensions = [Chain(302, (0, 0), (50, 0), (100, 0))]
        };
        var part = Part(7, solidComplete: false);
        var api = new TeklaDrawingDimensionDefectApi(
            _ => contexts,
            _ => [part],
            (_, modelId) => CandidateResult(modelId, solidComplete: true,
                (0, 0, DrawingPartCandidateConfidence.BoundingBoxFallback),
                (50, 0, DrawingPartCandidateConfidence.BoundingBoxFallback),
                (100, 0, DrawingPartCandidateConfidence.BoundingBoxFallback)));

        var report = api.GetDimensionDefects(12);

        Assert.True(part.SolidGeometryComplete);
        Assert.Contains(report.Defects, static defect =>
            defect.Kind == DimensionDefectKind.PhantomAnchor &&
            defect.Confidence == DimensionDefectConfidence.Mechanical);
        Assert.DoesNotContain(report.Defects, static defect => defect.Kind == DimensionDefectKind.AnchorUnverified);
    }

    private static DimensionContextInfo Chain(int dimensionId, params (double X, double Y)[] points) => new()
    {
        DimensionId = dimensionId,
        ViewId = 12,
        Orientation = "horizontal",
        PointAssociations = points.Select(point => new DimensionContextPointAssociationInfo
        {
            Point = new DrawingPointInfo { X = point.X, Y = point.Y }
        }).ToList()
    };

    private static PartGeometryInViewResult Part(int modelId, bool solidComplete) => new()
    {
        ModelId = modelId,
        PartPos = "T-" + modelId,
        BboxMin = [0, 0, 0],
        BboxMax = [100, 20, 10],
        SolidGeometryComplete = solidComplete,
        MaterialType = 5
    };

    private static GetPartCandidatePointsResult CandidateResult(
        int modelId,
        bool solidComplete,
        params (double X, double Y, DrawingPartCandidateConfidence Confidence)[] points) => new()
    {
        Success = true,
        ViewId = 12,
        ModelId = modelId,
        SolidGeometryComplete = solidComplete,
        Candidates = points.Select((point, index) => new DrawingPartCandidatePoint
        {
            ModelObjectId = modelId,
            Point = [point.X, point.Y, 0],
            Source = point.Confidence == DrawingPartCandidateConfidence.BoundingBoxFallback
                ? DrawingPartCandidatePointSource.BoundingBoxCorner
                : DrawingPartCandidatePointSource.SolidVertex,
            Confidence = point.Confidence,
            Anchor = new DrawingPartCandidateAnchor
            {
                ModelObjectId = modelId,
                Kind = point.Confidence == DrawingPartCandidateConfidence.BoundingBoxFallback
                    ? DrawingPartCandidateAnchorKind.BoundingBoxCorner
                    : DrawingPartCandidateAnchorKind.Vertex,
                Id = index.ToString()
            }
        }).ToList()
    };
}
