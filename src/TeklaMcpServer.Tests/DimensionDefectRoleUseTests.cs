using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.Dimensions.Defects;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// What the detector now reads about a part, and what it must not.
///
/// The three inline readings it replaced did not agree with each other, and the one that
/// consulted MATERIAL_TYPE counted insulation as frame - which is how two genuine overall
/// dimensions came to be reported as removable.
/// </summary>
public sealed class DimensionDefectRoleUseTests
{
    private static PartInView Part(
        int id, string mark, double maxX, PartRole role, int materialType = -1, bool classified = true) => new()
    {
        ModelId = id,
        PartPos = mark,
        PartPrefix = mark[..1],
        MaterialType = materialType,
        BboxMin = [0, 0, 0],
        BboxMax = [maxX, 100, 0],
        SolidGeometryComplete = true,
        Role = classified ? new PartRoleResult(role, "rule", "reason") : PartRoleResult.Unclassified
    };

    private static DimensionContextInfo Chain(params double[] xs) => new()
    {
        DimensionId = 1,
        Orientation = "horizontal",
        PointAssociations = xs
            .Select(static x => new DimensionContextPointAssociationInfo
            {
                Point = new DrawingPointInfo { X = x, Y = 0 }
            })
            .ToList()
    };

    private static DimensionDefectReport Run(IReadOnlyList<PartInView> parts, DimensionContextInfo chain) =>
        DimensionDefectDetector.Detect(1, [chain], parts, []);

    [Fact]
    public void InsulationDoesNotStretchTheStructuralExtent()
    {
        // The frame ends at 1000; the insulation runs 10 further. Measured over everything,
        // the 1000 chain stops being an overall and its containment exemption is lost.
        var parts = new[]
        {
            Part(1, "T-1", 1000, PartRole.Defining),
            Part(2, "R-1", 1010, PartRole.Attached)
        };

        var report = Run(parts, Chain(0, 1000));

        var summary = Assert.Single(report.Chains);
        Assert.True(summary.IsOverall);
    }

    [Fact]
    public void MaterialTypeFiveOnAttachedPartsChangesNothing()
    {
        // Insulation reports 5 exactly as timber does. Before, that alone put it in.
        var parts = new[]
        {
            Part(1, "T-1", 1000, PartRole.Defining, materialType: 5),
            Part(2, "R-1", 1010, PartRole.Attached, materialType: 5)
        };

        Assert.True(Assert.Single(Run(parts, Chain(0, 1000)).Chains).IsOverall);
    }

    [Fact]
    public void AnUnknownPartIsReportedRatherThanQuietlyLeftOut()
    {
        var parts = new[]
        {
            Part(1, "T-1", 1000, PartRole.Defining),
            Part(2, "Z-9", 1010, PartRole.Unknown)
        };

        var report = Run(parts, Chain(0, 1000));

        Assert.Contains(report.Warnings, warning => warning.Contains("structural extent is provisional"));
        Assert.Contains(report.Warnings, warning => warning.Contains("Z-9"));
    }

    [Fact]
    public void PartsArrivingWithoutARoleAreClassifiedRatherThanSkipped()
    {
        // Captured states carry no role, and hand-built fixtures rarely set one. Letting
        // the frame checks stop running on those would look exactly like clean drawings.
        var parts = new[]
        {
            Part(1, "T-1", 1000, PartRole.Unknown, classified: false),
            Part(2, "R-1", 1010, PartRole.Unknown, classified: false)
        };

        var report = Run(parts, Chain(0, 1000));

        // T and R are recognised on the spot, so the extent is the frame's and no
        // reservation is raised.
        Assert.True(Assert.Single(report.Chains).IsOverall);
        Assert.DoesNotContain(report.Warnings, warning => warning.Contains("structural extent is provisional"));
    }

    [Fact]
    public void DetectGivesBackTheSnapshotItWasHanded()
    {
        // It audits a snapshot. A caller that reads its own parts afterwards must not find
        // them quietly changed.
        var part = Part(1, "T-1", 1000, PartRole.Unknown, classified: false);
        var before = part.Role;

        DimensionDefectDetector.Detect(1, [Chain(0, 1000)], [part], []);

        Assert.Same(before, part.Role);
        Assert.False(part.Role.IsClassified);
    }

    [Fact]
    public void APartWithNoPropertiesStaysUnclassifiedRatherThanBecomingUnknown()
    {
        // A geometry-only copy dropped the properties a role comes from. Classifying it
        // anyway would report "no rule covers this" about a prefix nobody ever read.
        var geometryOnly = new PartInView
        {
            ModelId = 2,
            BboxMin = [0, 0, 0],
            BboxMax = [1010, 100, 0],
            SolidGeometryComplete = true
        };

        var report = DimensionDefectDetector.Detect(
            1, [Chain(0, 1000)], [Part(1, "T-1", 1000, PartRole.Defining), geometryOnly], []);

        var reservation = Assert.Single(
            report.Warnings.Where(warning => warning.Contains("structural extent is provisional")));

        Assert.Contains("never classified", reservation);
        Assert.DoesNotContain("matched no role rule", reservation);
    }

    [Fact]
    public void APrefixNoRuleCoversStillRaisesAReservation()
    {
        var parts = new[]
        {
            Part(1, "T-1", 1000, PartRole.Unknown, classified: false),
            Part(2, "Q-1", 1010, PartRole.Unknown, classified: false)
        };

        var report = Run(parts, Chain(0, 1000));

        Assert.Contains(report.Warnings, warning => warning.Contains("Q-1"));
    }
}
