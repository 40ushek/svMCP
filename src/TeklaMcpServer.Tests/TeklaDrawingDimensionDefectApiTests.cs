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
        var parts = new List<PartInView> { Part(7, solidComplete: true) };

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


    /// <summary>A contact spanning x = 0..100 at the foot of the view.</summary>
    private static SolidContacts.ContactGraph Seam() =>
        SolidContacts.ContactGraph.Build([SeamSlab(1, -10, 0), SeamSlab(2, 0, 10)]);

    private static SolidContacts.ISolid SeamSlab(int modelId, double z0, double z1)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ViewId = 12, ModelId = modelId };
        var corners = new (double X, double Y)[] { (0, 0), (100, 0), (100, 20), (0, 20) };
        var index = 0;

        foreach (var z in new[] { z0, z1 })
            foreach (var (x, y) in corners)
                geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = index++, Point = [x, y, z] });

        var bottom = new PartFaceGeometry { Index = 0, Normal = [0, 0, -1] };
        bottom.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });

        var top = new PartFaceGeometry { Index = 1, Normal = [0, 0, 1] };
        top.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [4, 5, 6, 7] });

        geometry.Solid.Faces.Add(bottom);
        geometry.Solid.Faces.Add(top);

        return ViewSolidAdapter.FromGeometry(geometry)!;
    }

    private static TeklaDrawingDimensionDefectApi ApiWith(
        GetDimensionContextsResult contexts,
        System.Func<int, ViewContactsResult>? getContacts) =>
        new(_ => contexts,
            // Wide enough that a point at x = 250 is inside the view. Narrower, and the
            // coordinate-space gate fires first and switches every other check off.
            _ => [new PartInView
            {
                ModelId = 7,
                PartPos = "T-7",
                BboxMin = [0, 0, 0],
                BboxMax = [300, 20, 10],
                SolidGeometryComplete = true,
                MaterialType = 5
            }],
            (_, modelId) => CandidateResult(modelId, solidComplete: true),
            getContacts);

    [Fact]
    public void ACompleteContactResultReachesTheDetector()
    {
        // The point at x = 250 is off the end of the seam, so the check can only fire if the
        // graph actually arrived.
        var contexts = new GetDimensionContextsResult { ViewId = 12, Dimensions = [Chain(101, (0, 0), (250, 0))] };
        var api = ApiWith(contexts, _ => new ViewContactsResult(12, Seam(), []));

        var report = api.GetDimensionDefects(12, withContacts: true);

        Assert.Contains(report.Signals, signal => signal.Kind == DimensionDefectKind.CoordinateWithoutContact);

        // Never among the defects: anything building a plan walks that list.
        Assert.DoesNotContain(report.Defects, defect => defect.Kind == DimensionDefectKind.CoordinateWithoutContact);
    }

    [Fact]
    public void AnIncompleteContactResultIsWarnedAboutAndNotUsed()
    {
        // A part of the view was never read, so an absent contact means nothing. Reporting the
        // check anyway would turn a gap in the data into a finding about the drawing.
        var contexts = new GetDimensionContextsResult { ViewId = 12, Dimensions = [Chain(101, (0, 0), (250, 0))] };
        var api = ApiWith(contexts, _ => new ViewContactsResult(12, Seam(), [new UnreadPart(9, "solid unavailable")]));

        var report = api.GetDimensionDefects(12, withContacts: true);

        Assert.Empty(report.Signals);
        Assert.Contains(report.Warnings, warning => warning.Contains("contacts not used"));
    }

    [Fact]
    public void AContactReadThatThrowsLeavesTheRestOfTheReportStanding()
    {
        var contexts = new GetDimensionContextsResult { ViewId = 12, Dimensions = [Chain(101, (0, 0), (250, 0))] };
        var api = ApiWith(contexts, _ => throw new System.InvalidOperationException("no drawing"));

        var report = api.GetDimensionDefects(12, withContacts: true);

        Assert.True(report.Success);
        Assert.Empty(report.Signals);
        Assert.Contains(report.Warnings, warning => warning.Contains("contacts not read"));
    }

    [Fact]
    public void ContactsAreNotSearchedUnlessAskedFor()
    {
        // A defect scan is expected to be quick, and what the search feeds is one weak signal.
        var contexts = new GetDimensionContextsResult { ViewId = 12, Dimensions = [Chain(101, (0, 0), (250, 0))] };
        var searched = false;

        var api = ApiWith(contexts, _ =>
        {
            searched = true;
            return new ViewContactsResult(12, Seam(), []);
        });

        var report = api.GetDimensionDefects(12);

        Assert.False(searched);
        Assert.Empty(report.Signals);
    }

    [Fact]
    public void WithNoContactSourceAtAllTheCheckSimplyDoesNotRun()
    {
        // How the captured states run: they carry no contacts, and that must stay silent.
        var contexts = new GetDimensionContextsResult { ViewId = 12, Dimensions = [Chain(101, (0, 0), (250, 0))] };
        var api = ApiWith(contexts, null);

        var report = api.GetDimensionDefects(12, withContacts: true);

        Assert.Empty(report.Signals);
        Assert.DoesNotContain(report.Warnings, warning => warning.Contains("contacts"));
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

    private static PartInView Part(int modelId, bool solidComplete) => new()
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
            ModelObjectIds = [modelId],
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
