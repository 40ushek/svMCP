using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.Dimensions.Defects;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The check that asks whether any two parts of the view meet at a chain point's coordinate.
///
/// Only the coordinate, and the tests hold it to that too - including one that pins the
/// weakness, so nobody reads the class as "this point rests on something". One direction only:
/// a contact at a coordinate with no point is not a finding, because most contacts are never
/// dimensioned and should not be.
/// </summary>
public sealed class CoordinateWithoutContactTests
{
    /// <summary>Two slabs meeting face to face at z, so the contact spans x from 0 to 100.</summary>
    private static ContactGraph TwoSlabsMeetingAt(double z)
    {
        return ContactGraph.Build([Slab(1, z - 50, z), Slab(2, z, z + 50)]);
    }

    private static ISolid Slab(int modelId, double z0, double z1)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ViewId = 1, ModelId = modelId };

        var corners = new (double X, double Y)[] { (0, 0), (100, 0), (100, 40), (0, 40) };
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

    private static DimensionContextInfo HorizontalChain(params double[] xs) => new()
    {
        DimensionId = 1,
        Orientation = "horizontal",
        PointAssociations = xs
            .Select(static x => new DimensionContextPointAssociationInfo
            {
                Point = new DrawingPointInfo { X = x, Y = -200 }
            })
            .ToList()
    };

    private static List<PartInView> Parts() =>
    [
        new()
        {
            ModelId = 1, PartPos = "T-1",
            BboxMin = [0d, 0, 0], BboxMax = [300d, 40, 100],
            SolidGeometryComplete = true
        }
    ];

    private static IReadOnlyList<DimensionDefect> Run(DimensionContextInfo chain, ContactGraph? contacts) =>
        DimensionDefectDetector
            .Detect(1, [chain], Parts(), [], contacts)
            .Signals
            .Where(static defect => defect.Kind == DimensionDefectKind.CoordinateWithoutContact)
            .ToList();

    [Fact]
    public void APointWhereTwoPartsMeetIsNotReported()
    {
        // The contact runs x = 0..100, so both ends of the chain sit on it.
        Assert.Empty(Run(HorizontalChain(0, 100), TwoSlabsMeetingAt(50)));
    }

    [Fact]
    public void APointWhereNothingMeetsIsReported()
    {
        var defect = Assert.Single(Run(HorizontalChain(0, 250), TwoSlabsMeetingAt(50)));

        Assert.Equal(250, defect.Point![0]);
        Assert.Contains("250", defect.Reason);
    }

    [Fact]
    public void TheFindingIsProvisionalBecauseAPointMayLegitimatelyStandClear()
    {
        // A free end or the outer corner of an assembly touches nothing, and is still where a
        // dimension belongs. The class can never be more than a prompt to look.
        var defect = Assert.Single(Run(HorizontalChain(0, 250), TwoSlabsMeetingAt(50)));

        Assert.Equal(DimensionDefectConfidence.Provisional, defect.Confidence);
    }

    [Fact]
    public void AContactWithNoPointIsNotAFinding()
    {
        // The contact spans x = 0..100 and the chain names neither end of it. That is ordinary:
        // most places two parts meet are never dimensioned, and the check never looks that way.
        Assert.Empty(Run(HorizontalChain(30, 60), TwoSlabsMeetingAt(50)));
    }

    [Fact]
    public void OneChainCanHaveASupportedPointAndAnUnsupportedOne()
    {
        // x = 45 sits part way along the seam, which runs the whole length: something meets
        // something there, so it is supported. Whether it is where a point BELONGS is a
        // different question and not this one. x = 250 is past the end of everything.
        var defect = Assert.Single(Run(HorizontalChain(45, 250), TwoSlabsMeetingAt(50)));

        Assert.Equal(250, defect.Point![0]);
    }

    [Fact]
    public void AContactElsewhereInTheViewStillCountsAndThatIsTheKnownWeakness()
    {
        // The seam lies at the bottom of the view; this point is 2000 above it and shares only
        // its X. The class is named for the coordinate precisely because of this: calling it
        // "the point rests on nothing" would be false in the other direction, and telling the
        // two apart needs a band tolerance the size of a member section.
        var chain = new DimensionContextInfo
        {
            DimensionId = 1,
            Orientation = "horizontal",
            PointAssociations =
            [
                new() { Point = new DrawingPointInfo { X = 50, Y = 2000 } },
                new() { Point = new DrawingPointInfo { X = 90, Y = 2000 } }
            ]
        };

        Assert.Empty(Run(chain, TwoSlabsMeetingAt(50)));
    }

    [Fact]
    public void WithoutContactsTheCheckDoesNotRun()
    {
        // The captured states carry no contacts, and silence there must not turn into findings.
        Assert.Empty(Run(HorizontalChain(0, 250), null));
    }

    [Fact]
    public void APointJustOffAFaceIsStillOnIt()
    {
        // A contact face and where a dimension actually snapped are not obliged to agree
        // exactly; 0.3 was observed on a real chain.
        Assert.Empty(Run(HorizontalChain(0.3, 99.7), TwoSlabsMeetingAt(50)));
    }
}
