using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionSnapshotProjectionTests
{
    [Fact]
    public void ProjectDimensionSnapshotToReadModel_PreservesSnapshotSemantics()
    {
        var snapshot = new TeklaDimensionSetSnapshot
        {
            Id = 42,
            Type = "StraightDimensionSet",
            TeklaDimensionType = "Absolute",
            ViewId = 17,
            ViewType = "FrontView",
            ViewScale = 20.0,
            Orientation = "horizontal",
            Distance = -12.5,
            DirectionX = 1.0,
            DirectionY = 0.0,
            TopDirection = -1,
            Bounds = new DrawingBoundsInfo { MinX = 1, MinY = 2, MaxX = 3, MaxY = 4 },
            ReferenceLine = new DrawingLineInfo { StartX = 10, StartY = 20, EndX = 30, EndY = 20 },
            SourceKind = DimensionSourceKind.Part,
            GeometryKind = DimensionGeometryKind.Horizontal,
            ClassifiedDimensionType = DimensionType.Horizontal
        };

        snapshot.MeasuredPoints.Add(new DrawingPointInfo { X = 100, Y = 200, Order = 0 });
        snapshot.MeasuredPoints.Add(new DrawingPointInfo { X = 300, Y = 200, Order = 1 });
        snapshot.SourceReferences.Add(new DimensionSourceReference
        {
            SourceKind = DimensionSourceKind.Part,
            DrawingObjectId = 501,
            ModelId = 9001
        });
        snapshot.Segments.Add(new TeklaDimensionSegmentSnapshot
        {
            Id = 1001,
            StartX = 100,
            StartY = 200,
            EndX = 300,
            EndY = 200,
            Distance = -12.5,
            DirectionX = 1.0,
            DirectionY = 0.0,
            TopDirection = -1,
            Bounds = new DrawingBoundsInfo { MinX = 100, MinY = 190, MaxX = 300, MaxY = 210 },
            TextBounds = new DrawingBoundsInfo { MinX = 180, MinY = 205, MaxX = 220, MaxY = 215 },
            DimensionLine = new DrawingLineInfo { StartX = 100, StartY = 220, EndX = 300, EndY = 220 },
            LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 200, EndX = 100, EndY = 220 },
            LeadLineSecond = new DrawingLineInfo { StartX = 300, StartY = 200, EndX = 300, EndY = 220 }
        });

        var info = TeklaDrawingDimensionsApi.ProjectDimensionSnapshotToReadModel(snapshot);

        Assert.Equal(snapshot.Id, info.Id);
        Assert.Equal(snapshot.Type, info.Type);
        Assert.Equal(snapshot.TeklaDimensionType, info.DimensionType);
        Assert.Equal(snapshot.ViewId, info.ViewId);
        Assert.Equal(snapshot.ViewType, info.ViewType);
        Assert.Equal(snapshot.ViewScale, info.ViewScale);
        Assert.Equal(snapshot.Orientation, info.Orientation);
        Assert.Equal(snapshot.Distance, info.Distance);
        Assert.Equal(snapshot.DirectionX, info.DirectionX);
        Assert.Equal(snapshot.DirectionY, info.DirectionY);
        Assert.Equal(snapshot.TopDirection, info.TopDirection);
        Assert.Equal(snapshot.SourceKind, info.SourceKind);
        Assert.Equal(snapshot.GeometryKind, info.GeometryKind);
        Assert.Equal(snapshot.ClassifiedDimensionType, info.ClassifiedDimensionType);
        var source = Assert.Single(info.SourceReferences);
        Assert.Equal(DimensionSourceKind.Part, source.SourceKind);
        Assert.Equal(501, source.DrawingObjectId);
        Assert.Equal(9001, source.ModelId);
        Assert.Equal(snapshot.MeasuredPoints.Count, info.MeasuredPoints.Count);

        var segment = Assert.Single(info.Segments);
        Assert.Equal(1001, segment.Id);
        Assert.Equal(100, segment.StartX);
        Assert.Equal(300, segment.EndX);
        Assert.NotNull(segment.TextBounds);
        Assert.NotNull(segment.DimensionLine);
        Assert.NotNull(segment.LeadLineMain);
        Assert.NotNull(segment.LeadLineSecond);
    }

    [Fact]
    public void BuildDimensionSnapshotFingerprint_IgnoresInputOrder()
    {
        var first = CreateSnapshot(20, 2002, 2001);
        var second = CreateSnapshot(10, 1002, 1001);

        var fingerprintA = TeklaDrawingDimensionsApi.BuildDimensionSnapshotFingerprint([first, second]);
        var fingerprintB = TeklaDrawingDimensionsApi.BuildDimensionSnapshotFingerprint([second, first]);

        Assert.Equal(fingerprintA, fingerprintB);
    }

    [Fact]
    public void BuildGroups_FromSnapshots_UsesSnapshotPathWithoutReadModelProjection()
    {
        var snapshot = new TeklaDimensionSetSnapshot
        {
            Id = 42,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            TeklaDimensionType = "Absolute",
            Orientation = "horizontal",
            Distance = 20,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            SourceKind = DimensionSourceKind.Part,
            GeometryKind = DimensionGeometryKind.Horizontal,
            ClassifiedDimensionType = DimensionType.Horizontal,
            ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -20, EndX = 100, EndY = -20 }
        };

        snapshot.MeasuredPoints.Add(new DrawingPointInfo { X = 0, Y = 0, Order = 0 });
        snapshot.MeasuredPoints.Add(new DrawingPointInfo { X = 100, Y = 0, Order = 1 });
        snapshot.SourceReferences.Add(new DimensionSourceReference
        {
            SourceKind = DimensionSourceKind.Part,
            DrawingObjectId = 5001,
            ModelId = 101
        });
        snapshot.Segments.Add(new TeklaDimensionSegmentSnapshot
        {
            Id = 4201,
            StartX = 0,
            StartY = 0,
            EndX = 100,
            EndY = 0,
            Distance = 20,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            DimensionLine = new DrawingLineInfo { StartX = 0, StartY = -20, EndX = 100, EndY = -20 },
            LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -20 },
            LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -20 },
            TextBounds = new DrawingBoundsInfo { MinX = 35, MinY = -28, MaxX = 65, MaxY = -18 }
        });

        var group = Assert.Single(DimensionGroupFactory.BuildGroups([snapshot]));
        var item = Assert.Single(group.DimensionList);

        Assert.Equal(42, item.DimensionId);
        Assert.Equal(15, item.ViewScale);
        Assert.Equal("horizontal", item.Orientation);
        var source = Assert.Single(item.SourceReferences);
        Assert.Equal(5001, source.DrawingObjectId);
        Assert.Equal(101, source.ModelId);
        Assert.Equal(2, item.MeasuredPoints.Count);
        Assert.Single(item.Segments);
    }

    private static TeklaDimensionSetSnapshot CreateSnapshot(int id, params int[] segmentIds)
    {
        var snapshot = new TeklaDimensionSetSnapshot
        {
            Id = id,
            Distance = 15.25,
            ReferenceLine = new DrawingLineInfo { StartX = id, StartY = 0, EndX = id + 10, EndY = 0 }
        };

        foreach (var segmentId in segmentIds)
        {
            snapshot.Segments.Add(new TeklaDimensionSegmentSnapshot
            {
                Id = segmentId,
                StartX = segmentId,
                EndX = segmentId + 1
            });
        }

        return snapshot;
    }
}
