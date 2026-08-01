using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The mapper is a hand-written copy from the internal context to the public DTO, so a field
/// added to one side does not reach the other on its own. `PartPrefix` was already lost that way
/// once — present on the part, absent from the view context.
/// </summary>
public sealed class DrawingViewContextMapperTests
{
    [Fact]
    public void ToResult_CarriesViewIdentityIncludingViewType()
    {
        var context = new DrawingViewContext
        {
            ViewId = 3599,
            ViewType = "SectionView",
            ViewScale = 25
        };

        var result = DrawingViewContextMapper.ToResult(context);

        Assert.True(result.Success);
        Assert.Equal(3599, result.ViewId);
        Assert.Equal("SectionView", result.ViewType);
        Assert.Equal(25, result.ViewScale);
    }

    [Fact]
    public void ToResult_CarriesPartClassificationFields()
    {
        // partPrefix and materialType are what candidate filtering runs on — T timber is
        // structural, R insulation and M fittings are not — so losing them in the mapper would
        // silently disable that filter downstream.
        var context = new DrawingViewContext { ViewId = 1, ViewType = "FrontView", ViewScale = 1 };
        context.Parts.Add(new PartGeometryInViewResult
        {
            Success = true,
            ViewId = 1,
            ModelId = 12345,
            Type = "Beam",
            Name = "BEAM",
            PartPos = "T-368",
            Profile = "60X240",
            Material = "C24",
            MaterialType = 5,
            PartPrefix = "T",
            SolidGeometryComplete = true,
            SolidVertices = [[0d, 0d, 0d], [10d, 0d, 0d]],
            ViewHull = [[0d, 0d], [10d, 0d]]
        });

        var part = Assert.Single(DrawingViewContextMapper.ToResult(context).Parts);

        Assert.Equal(12345, part.ModelId);
        Assert.Equal("T-368", part.PartPos);
        Assert.Equal("60X240", part.Profile);
        Assert.Equal("C24", part.Material);
        Assert.Equal(5, part.MaterialType);
        Assert.Equal("T", part.PartPrefix);
        Assert.Equal([[0d, 0d], [10d, 0d]], part.ViewHull);
        Assert.True(part.SolidGeometryComplete);
    }

    [Fact]
    public void ToResult_CopiesCollectionsInsteadOfSharingThem()
    {
        // The DTO crosses the bridge boundary; sharing the context's own lists would let a later
        // mutation of the context change a payload that was already handed out.
        var context = new DrawingViewContext { ViewId = 1, ViewType = "FrontView", ViewScale = 1 };
        context.GridIds.Add("grid-a");
        context.Warnings.Add("warning-a");

        var result = DrawingViewContextMapper.ToResult(context);
        context.GridIds.Add("grid-b");
        context.Warnings.Add("warning-b");

        Assert.Equal(["grid-a"], result.GridIds);
        Assert.Equal(["warning-a"], result.Warnings);
    }
}

/// <summary>
/// The view type for a context is taken from the dimension items rather than re-read from Tekla.
/// Consumers branch on it — a section is dimensioned unlike a front view — so a wrong value is
/// worse than none.
/// </summary>
public sealed class ContextViewTypeResolutionTests
{
    [Fact]
    public void SingleValue_IsUsed()
    {
        var warnings = new List<string>();

        var viewType = TeklaDrawingDimensionsApi.ResolveContextViewType(
            ["FrontView", "FrontView", "  "],
            warnings);

        Assert.Equal("FrontView", viewType);
        Assert.Empty(warnings);
    }

    [Fact]
    public void DisagreeingValues_YieldNothingRatherThanTheFirst()
    {
        // Taking the first would let a stale item decide the type of the whole context, and the
        // caller would act on it. Empty is honestly unknown.
        var warnings = new List<string>();

        var viewType = TeklaDrawingDimensionsApi.ResolveContextViewType(
            ["SectionView", "FrontView"],
            warnings);

        Assert.Equal(string.Empty, viewType);
        Assert.Equal(["view_type_inconsistent"], warnings);
    }

    [Fact]
    public void NoValues_AreReportedSeparately()
    {
        // Missing and contradictory are different problems and must not share one warning.
        var warnings = new List<string>();

        var viewType = TeklaDrawingDimensionsApi.ResolveContextViewType(["", "   "], warnings);

        Assert.Equal(string.Empty, viewType);
        Assert.Equal(["view_type_unavailable"], warnings);
    }
}
