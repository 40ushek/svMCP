using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class CalcDimensionChainsTests
{
    [Fact]
    public void ARectangularGroupGetsFourChainsWithItsOverallExtremes()
    {
        var rectangle = Shape("part:1", false, (0, 0), (100, 0), (100, 50), (0, 50));
        var group = new GeometryGroup("wood-frame", [rectangle], [rectangle]);

        CalcDimensionChains.Apply(group);

        var chains = Assert.IsType<DimensionChainSet>(group.DimensionChains);
        Assert.Equal(DimensionChainSetStage.Calculated, chains.Stage);
        Assert.Equal([0d, 100d], Coordinates(chains, DimensionChainSide.Top));
        Assert.Equal([0d, 100d], Coordinates(chains, DimensionChainSide.Bottom));
        Assert.Equal([0d, 50d], Coordinates(chains, DimensionChainSide.Left));
        Assert.Equal([0d, 50d], Coordinates(chains, DimensionChainSide.Right));
        Assert.Equal(new Vec3(1, 0, 0), chains[DimensionChainSide.Top].Direction);
        Assert.Equal(new Vec3(0, 1, 0), chains[DimensionChainSide.Top].PlacementNormal);
    }

    [Fact]
    public void APartKeepsBothFacesUntilPolicyChoosesOne()
    {
        var boundary = Shape("outline", false, (0, 0), (200, 0), (200, 100), (0, 100));
        var stud = Shape("part:stud", false, (60, 0), (70, 0), (70, 100), (60, 100));
        var group = new GeometryGroup("wood-frame", [boundary], [stud]);

        CalcDimensionChains.Apply(group);

        Assert.Equal([0d, 60d, 70d, 200d], Coordinates(group.DimensionChains!, DimensionChainSide.Top));
        Assert.Equal([0d, 60d, 70d, 200d], Coordinates(group.DimensionChains!, DimensionChainSide.Bottom));
    }

    [Fact]
    public void BoundarySuppliesOnlyTheOverallBoxNotExtraChainPositions()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (60, 100), (60, 60), (0, 60));
        var member = Shape("bolt:1", false, (20, 20));
        var group = new GeometryGroup("assembly", [boundary], [member]);

        CalcDimensionChains.Apply(group);

        Assert.Equal([0d, 100d], Coordinates(group.DimensionChains!, DimensionChainSide.Top));
        Assert.Equal([0d, 20d, 100d], Coordinates(group.DimensionChains!, DimensionChainSide.Bottom));
        Assert.DoesNotContain(group.DimensionChains![DimensionChainSide.Bottom].Positions.SelectMany(position => position.Supports), support =>
            ReferenceEquals(support.Source, boundary) && support.Kind != DimensionChainPositionSupportKind.GroupExtent);
        Assert.All(group.DimensionChains.Chains.SelectMany(chain => chain.Positions)
            .SelectMany(position => position.Supports)
            .Where(support => ReferenceEquals(support.Source, boundary)), support =>
            Assert.Null(support.ModelId));
    }

    [Fact]
    public void PartSupportCarriesItsModelIdAndExtentAcrossAllOfItsRings()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 50), (0, 50));
        var outer = Shape("part:11:outer", false, modelId: 11, (0, 0), (100, 0), (100, 50), (0, 50));
        var hole = Shape("part:11:hole", true, modelId: 11, (40, 10), (60, 10), (60, 40), (40, 40));
        var group = new GeometryGroup("one-part", [boundary], [outer, hole]);

        CalcDimensionChains.Apply(group);

        var support = Assert.Single(Position(group.DimensionChains!, DimensionChainSide.Top, 0).Supports,
            item => ReferenceEquals(item.Source, outer));
        Assert.Equal(11, support.ModelId);
        Assert.Equal(0d, support.AxisAlignedModelExtent!.MinX);
        Assert.Equal(100d, support.AxisAlignedModelExtent.MaxX);
        Assert.Equal(0d, support.AxisAlignedModelExtent.MinY);
        Assert.Equal(50d, support.AxisAlignedModelExtent.MaxY);
    }

    [Fact]
    public void ARakedPartNeverSuppliesABoundingBoxPartSpan()
    {
        var boundary = Shape("outline", false, (0, 0), (200, 0), (200, 100), (0, 100));
        var rakedPart = Shape("part:raked", false, modelId: 11, (0, 0), (100, 0), (100, 100));
        var group = new GeometryGroup("raked", [boundary], [rakedPart]);

        CalcDimensionChains.Apply(group);

        Assert.All(group.DimensionChains!.Chains.SelectMany(chain => chain.Positions)
            .SelectMany(position => position.Supports)
            .Where(support => support.ModelId == 11), support =>
            Assert.Null(support.AxisAlignedModelExtent));
    }

    [Fact]
    public void ProjectionNoiseDoesNotHideAnOtherwiseAxisAlignedPartExtent()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (0, 100));
        var noisyRectangle = Shape("part:noisy", false, modelId: 11,
            (0, 0), (60, 0.002), (60, 100), (0, 100));
        var group = new GeometryGroup("noisy", [boundary], [noisyRectangle]);

        CalcDimensionChains.Apply(group);

        Assert.Contains(group.DimensionChains!.Chains.SelectMany(chain => chain.Positions)
            .SelectMany(position => position.Supports), support =>
            support.ModelId == 11 && support.AxisAlignedModelExtent != null);
    }

    [Fact]
    public void AShortDiagonalIsNotMistakenForAnAxisAlignedPart()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (0, 100));
        var diagonal = Shape("part:diagonal", false, modelId: 11, (0, 0), (0.001, 0.001));
        var group = new GeometryGroup("short-diagonal", [boundary], [diagonal]);

        CalcDimensionChains.Apply(group);

        Assert.All(group.DimensionChains!.Chains.SelectMany(chain => chain.Positions)
            .SelectMany(position => position.Supports)
            .Where(support => support.ModelId == 11), support =>
            Assert.Null(support.AxisAlignedModelExtent));
    }

    [Fact]
    public void AnEmptyPartSourceDoesNotFailTheWholeCalculation()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (0, 100));
        var emptyPart = new GeometryGroupShape("part:empty", PlanarShape.Empty, modelId: 11);
        var group = new GeometryGroup("with-empty-part", [boundary], [emptyPart]);

        CalcDimensionChains.Apply(group);

        Assert.NotNull(group.DimensionChains);
    }

    [Fact]
    public void ASlopedEdgeContributesThroughItsCornersNotAnInventedEdgeCoordinate()
    {
        var triangle = Shape("part:raked", false, (0, 0), (100, 0), (100, 100));
        var group = new GeometryGroup("wood-frame", [triangle], [triangle]);

        CalcDimensionChains.Apply(group);

        var bottomAtZero = Position(group.DimensionChains!, DimensionChainSide.Bottom, 0);
        Assert.Contains(bottomAtZero.Supports, support =>
            support.Kind == DimensionChainPositionSupportKind.TiltedEdgeCorner &&
            support.Point.Equals(new Vec3(0, 0, 0)));

        var rightAtHundred = Position(group.DimensionChains!, DimensionChainSide.Right, 100);
        Assert.Contains(rightAtHundred.Supports, support =>
            support.Kind == DimensionChainPositionSupportKind.TiltedEdgeCorner &&
            support.Point.Equals(new Vec3(100, 100, 0)));
    }

    [Fact]
    public void ASlopedSegmentContributesBothEndpointAxes()
    {
        var boundary = Shape("outline", false, (0, 0), (200, 0), (200, 1000), (0, 1000));
        var contact = Shape("contact:1", false, (30, 0), (130, 1000)); // 5.7 degrees from vertical
        var group = new GeometryGroup("contacts", [boundary], [contact]);

        CalcDimensionChains.Apply(group);

        Assert.Contains(group.DimensionChains![DimensionChainSide.Top].Positions.SelectMany(position => position.Supports), support =>
            ReferenceEquals(support.Source, contact) && support.Kind == DimensionChainPositionSupportKind.SegmentEndpoint);
        Assert.Contains(group.DimensionChains[DimensionChainSide.Left].Positions.SelectMany(position => position.Supports), support =>
            ReferenceEquals(support.Source, contact) && support.Kind == DimensionChainPositionSupportKind.SegmentEndpoint);
        Assert.Contains(group.DimensionChains[DimensionChainSide.Right].Positions.SelectMany(position => position.Supports), support =>
            ReferenceEquals(support.Source, contact) && support.Kind == DimensionChainPositionSupportKind.SegmentEndpoint);
        Assert.DoesNotContain(group.DimensionChains.Chains.SelectMany(chain => chain.Positions).SelectMany(position => position.Supports), support =>
            ReferenceEquals(support.Source, contact) && support.Kind == DimensionChainPositionSupportKind.AxisAlignedEdge);
    }

    [Fact]
    public void HoleEvidenceSurvivesIntoThePositionsItSupports()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (0, 100));
        var hole = Shape("part:1/hole:1", true, (40, 40), (60, 40), (60, 60), (40, 60));
        var group = new GeometryGroup("single-part", [boundary], [hole]);

        CalcDimensionChains.Apply(group);

        var topAtForty = Position(group.DimensionChains!, DimensionChainSide.Top, 40);
        Assert.Contains(topAtForty.Supports, support => support.Source.IsHole);

        var leftAtForty = Position(group.DimensionChains!, DimensionChainSide.Left, 40);
        Assert.Contains(leftAtForty.Supports, support => support.Source.IsHole);
    }

    [Fact]
    public void AGroupWithoutBoundaryUsesOnlyActualPointCoordinatesForItsExtremes()
    {
        var firstBolt = Shape("bolt:1", false, (10, 20));
        var secondBolt = Shape("bolt:2", false, (40, 80));
        var group = new GeometryGroup("bolts", boundaryShapes: null, [firstBolt, secondBolt]);

        CalcDimensionChains.Apply(group);

        var chains = group.DimensionChains!;
        Assert.Equal([10d, 40d], Coordinates(chains, DimensionChainSide.Top));
        Assert.Equal([20d, 80d], Coordinates(chains, DimensionChainSide.Left));
        Assert.All(chains.Chains.SelectMany(chain => chain.Positions).SelectMany(position => position.Supports), support =>
            Assert.Contains(support.Point, new[] { new Vec3(10, 20, 0), new Vec3(40, 80, 0) }));
    }

    [Fact]
    public void ExtentSupportsUseTheSameCoincidenceToleranceAsChainPositions()
    {
        var first = Shape("bolt:1", false, (0, 0));
        var nearFirst = Shape("bolt:2", false, (0.00075, 0));
        var last = Shape("bolt:3", false, (0.0015, 0));
        var group = new GeometryGroup("bolts", boundaryShapes: null, [first, nearFirst, last]);

        CalcDimensionChains.Apply(group);

        Assert.Contains(Position(group.DimensionChains!, DimensionChainSide.Top, 0).Supports, support =>
            ReferenceEquals(support.Source, nearFirst) && support.Kind == DimensionChainPositionSupportKind.GroupExtent);
        Assert.Contains(Position(group.DimensionChains!, DimensionChainSide.Top, 0.0015).Supports, support =>
            ReferenceEquals(support.Source, nearFirst) && support.Kind == DimensionChainPositionSupportKind.GroupExtent);
    }

    [Fact]
    public void AnEmptyExtentFailsInsteadOfReturningFourEmptyChains()
    {
        var emptyBoundary = new GeometryGroupShape("outline", PlanarShape.Empty);
        var group = new GeometryGroup("empty-group", [emptyBoundary]);

        var error = Assert.Throws<InvalidOperationException>(() => CalcDimensionChains.Apply(group));

        Assert.Contains("empty-group", error.Message);
        Assert.Null(group.DimensionChains);
    }

    [Fact]
    public void AnEmptyMemberShapeIsIntentionallyIgnoredWhenOtherEvidenceExists()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (0, 100));
        var empty = new GeometryGroupShape("unflattened-contact", PlanarShape.Empty);
        var group = new GeometryGroup("wood-frame", [boundary], [empty]);

        CalcDimensionChains.Apply(group);

        Assert.Equal([0d, 100d], Coordinates(group.DimensionChains!, DimensionChainSide.Top));
    }

    [Fact]
    public void ASourceExactlyAtTheCentreRemainsOnBothPreliminarySides()
    {
        var boundary = Shape("outline", false, (0, 0), (100, 0), (100, 100), (0, 100));
        var bolt = Shape("bolt:1", false, (50, 50));
        var group = new GeometryGroup("bolts", [boundary], [bolt]);

        CalcDimensionChains.Apply(group);

        foreach (var side in new[]
                 {
                     DimensionChainSide.Top,
                     DimensionChainSide.Bottom,
                     DimensionChainSide.Left,
                     DimensionChainSide.Right
                 })
        {
            Assert.Contains(group.DimensionChains![side].Positions.SelectMany(position => position.Supports), support =>
                ReferenceEquals(support.Source, bolt) && support.Kind == DimensionChainPositionSupportKind.PointShape);
        }
    }

    [Fact]
    public void PolicyDispositionKeepsTheReasonForARemovedPosition()
    {
        var rectangle = Shape("part:1", false, (0, 0), (100, 0), (100, 50), (0, 50));
        var group = new GeometryGroup("wood-frame", [rectangle], [rectangle]);
        CalcDimensionChains.Apply(group);

        var position = Position(group.DimensionChains!, DimensionChainSide.Top, 100);
        position.MarkRemoved("part thickness repeats fabrication size");
        foreach (var other in group.DimensionChains!.Chains.SelectMany(chain => chain.Positions)
                     .Where(other => other != position))
        {
            other.MarkKept("retained by the initial policy example");
        }
        group.DimensionChains!.MarkPolicyApplied();

        Assert.Equal(DimensionChainPositionDisposition.Removed, position.Disposition);
        Assert.Equal("part thickness repeats fabrication size", position.DispositionReason);
        Assert.Equal(DimensionChainSetStage.PolicyApplied, group.DimensionChains.Stage);
    }

    [Fact]
    public void PolicyStageCannotHideUndecidedPositions()
    {
        var rectangle = Shape("part:1", false, (0, 0), (100, 0), (100, 50), (0, 50));
        var group = new GeometryGroup("wood-frame", [rectangle], [rectangle]);
        CalcDimensionChains.Apply(group);

        Assert.Throws<InvalidOperationException>(() => group.DimensionChains!.MarkPolicyApplied());
        Assert.Equal(DimensionChainSetStage.Calculated, group.DimensionChains!.Stage);
    }

    [Fact]
    public void ApplyingAgainCannotDiscardPolicyDecisions()
    {
        var rectangle = Shape("part:1", false, (0, 0), (100, 0), (100, 50), (0, 50));
        var group = new GeometryGroup("wood-frame", [rectangle], [rectangle]);
        CalcDimensionChains.Apply(group);
        var chains = group.DimensionChains!;

        foreach (var position in chains.Chains.SelectMany(chain => chain.Positions))
            position.MarkKept("reviewed");
        chains.MarkPolicyApplied();

        Assert.Throws<InvalidOperationException>(() => CalcDimensionChains.Apply(group));
        Assert.Same(chains, group.DimensionChains);
        Assert.Equal(DimensionChainSetStage.PolicyApplied, group.DimensionChains!.Stage);
    }

    [Fact]
    public void DebugLinesSeparateTopAndBottomEvenWhenTheyShareCoordinates()
    {
        var first = Shape("part:1", false, (0, 0), (10, 0), (10, 10), (0, 10));
        var second = Shape("part:2", false, (100, 20), (110, 20), (110, 30), (100, 30));
        var group = new GeometryGroup("disconnected", [first, second], [first, second]);
        CalcDimensionChains.Apply(group);

        var bottomLines = DimensionChainDebugOverlayBuilder.CreateLines(group, DimensionChainSide.Bottom, viewId: 7);
        var topLines = DimensionChainDebugOverlayBuilder.CreateLines(group, DimensionChainSide.Top, viewId: 7);

        Assert.All(bottomLines, line =>
        {
            Assert.Equal(line.X1, line.X2);
            Assert.Equal(0d, line.Y1);
            Assert.Equal(15d, line.Y2);
            Assert.Equal(7, line.ViewId);
            Assert.Equal("blue", line.Color);
        });
        Assert.All(topLines, line =>
        {
            Assert.Equal(line.X1, line.X2);
            Assert.Equal(15d, line.Y1);
            Assert.Equal(30d, line.Y2);
            Assert.Equal(7, line.ViewId);
            Assert.Equal("red", line.Color);
        });
        Assert.Equal(Coordinates(group.DimensionChains!, DimensionChainSide.Bottom), bottomLines.Select(line => line.X1));
        Assert.Equal(Coordinates(group.DimensionChains!, DimensionChainSide.Top), topLines.Select(line => line.X1));
    }

    private static GeometryGroupShape Shape(string id, bool isHole, params (double X, double Y)[] points) =>
        Shape(id, isHole, modelId: null, points);

    private static GeometryGroupShape Shape(
        string id,
        bool isHole,
        int? modelId,
        params (double X, double Y)[] points) =>
        new(id, RegionFlattener.Flatten(points.Select(point => new Vec3(point.X, point.Y, 0)).ToList()), isHole, modelId);

    private static double[] Coordinates(DimensionChainSet chains, DimensionChainSide side) =>
        chains[side].Positions.Select(position => position.Coordinate).ToArray();

    private static DimensionChainPosition Position(DimensionChainSet chains, DimensionChainSide side, double coordinate) =>
        Assert.Single(chains[side].Positions, position => position.Coordinate == coordinate);
}
