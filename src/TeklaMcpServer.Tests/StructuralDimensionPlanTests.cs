using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class StructuralDimensionPlanTests
{
    private static (GeometryGroup Group, StructuralDimensionPlan Plan) Fixture()
    {
        GeometryGroupShape Rectangle(int id, double x0, double x1, double y0, double y1) =>
            new($"part:{id}", RegionFlattener.Flatten(new[]
            { new Vec3(x0,y0,0), new Vec3(x1,y0,0), new Vec3(x1,y1,0), new Vec3(x0,y1,0) }), modelId: id);
        var plate = Rectangle(2, 0, 260, 0, 340);
        var member = Rectangle(1, 50, 210, 90, 250);
        var boundary = new GeometryGroupShape("boundary", plate.Shape);
        var group = new GeometryGroup("assembly", [boundary], [member, plate]);
        CalcDimensionChains.Apply(group);
        var plan = new StructuralDimensionPlan
        {
            ViewId = 17, Side = "Bottom", ReferenceModelId = 1, SubjectModelId = 2,
            SourceFingerprint = StructuralDimensionPlanBuilder.Fingerprint(group.DimensionChains!, "drawing1"),
            RuleSet = "steel", Purpose = "PartLocation", InternalPolicy = "Necessary", RecognizableDistance = 1,
            DatumReason = "left plate edge", ClosureReason = "close on both plate edges around main part",
            RowType = "Relative", AttributesFile = "standard", Distance = 20
        };
        plan.Positions = group.DimensionChains![DimensionChainSide.Bottom].Positions.Select((p, i) =>
            new DimensionPositionDecision { PositionIndex = i, Disposition = "Kept", Reason = "locate plate against member",
                SupportIndex = p.Supports.ToList().FindIndex(s => s.ModelId.HasValue) }).ToList();
        return (group, plan);
    }

    private static PreparedDimensionPlan Prepare(GeometryGroup group, StructuralDimensionPlan plan) =>
        StructuralDimensionPlanBuilder.Prepare(plan, group,
            StructuralDimensionPlanBuilder.Fingerprint(group.DimensionChains!, "drawing1"), [1], true);

    [Fact]
    public void PlateLocationHasFiftyOneSixtyFiftyWithoutRequiringClosureOnMainPart()
    {
        var (group, plan) = Fixture();
        var prepared = Prepare(group, plan);
        Assert.Equal(new[] { 0d, 50d, 210d, 260d }, prepared.Points.Where((_, i) => i % 3 == 0));
        Assert.True(prepared.Points.Where((_, i) => i % 3 == 1).Distinct().Count() > 1);
        Assert.Equal("horizontal-down", prepared.Direction);
        Assert.Equal(DimensionChainSetStage.PolicyApplied, prepared.ReviewedChain.Stage);
        Assert.Equal(DimensionChainSetStage.Calculated, group.DimensionChains!.Stage);
    }

    [Fact]
    public void PlateSizeAloneDoesNotLocatePlate()
    {
        var (group, plan) = Fixture();
        foreach (var decision in plan.Positions.Skip(1).Take(2))
        { decision.Disposition = "Removed"; decision.SupportIndex = null; }
        Assert.Contains("size alone", Assert.Throws<ArgumentException>(() => Prepare(group, plan)).Message);
    }

    [Fact]
    public void RejectsStaleSourceAndChangedDrawing()
    {
        var (group, plan) = Fixture();
        Assert.Throws<InvalidOperationException>(() => StructuralDimensionPlanBuilder.Prepare(plan, group, "other", [1], true));
        Assert.NotEqual(plan.SourceFingerprint, StructuralDimensionPlanBuilder.Fingerprint(group.DimensionChains!, "drawing2"));
    }

    [Fact]
    public void EveryPositionNeedsExactlyOneExplainedDecision()
    {
        var (group, plan) = Fixture();
        plan.Positions[0].Reason = "";
        Assert.Throws<ArgumentException>(() => Prepare(group, plan));
        plan.Positions.RemoveAt(0);
        Assert.Throws<ArgumentException>(() => Prepare(group, plan));
        Assert.All(group.DimensionChains!.Chains.SelectMany(c => c.Positions), p =>
            Assert.Equal(DimensionChainPositionDisposition.Calculated, p.Disposition));
    }

    [Fact]
    public void GroupExtentCannotBeUsedAsMainPartSupport()
    {
        var (group, plan) = Fixture();
        var index = group.DimensionChains![DimensionChainSide.Bottom].Positions[0].Supports.ToList().FindIndex(s => !s.ModelId.HasValue);
        Assert.True(index >= 0);
        plan.Positions[0].SupportIndex = index;
        Assert.Throws<ArgumentException>(() => Prepare(group, plan));
    }

    [Fact]
    public void ReverseStartChangesPointsAndApprovalToken()
    {
        var (group, plan) = Fixture();
        var first = Prepare(group, plan);
        plan.ReverseStart = true;
        var reversed = Prepare(group, plan);
        Assert.Equal(260d, reversed.Points[0]);
        Assert.NotEqual(first.ApprovalToken, reversed.ApprovalToken);
    }

    [Fact]
    public void ChangedDecisionOrOffsetRequiresAnotherPreview()
    {
        var (group, plan) = Fixture();
        var first = Prepare(group, plan);
        plan.Distance++;
        Assert.NotEqual(first.ApprovalToken, Prepare(group, plan).ApprovalToken);
        plan.Distance--;
        plan.Positions[0].Reason = "different reason";
        Assert.NotEqual(first.ApprovalToken, Prepare(group, plan).ApprovalToken);
    }

    [Fact]
    public void UnknownMainPartAndUnsupportedRunningRowsBlock()
    {
        var (group, plan) = Fixture();
        Assert.Throws<InvalidOperationException>(() => StructuralDimensionPlanBuilder.Prepare(plan, group, plan.SourceFingerprint, [1], false));
        plan.RowType = "Absolute";
        Assert.Throws<ArgumentException>(() => Prepare(group, plan));
    }

    [Fact]
    public void NecessaryNeedsExplicitTolerance()
    {
        var (group, plan) = Fixture();
        plan.RecognizableDistance = null;
        Assert.Throws<ArgumentException>(() => Prepare(group, plan));
    }

    [Fact]
    public void NoneDoesNotAdmitMultipleSubjectPositions()
    {
        var (group, plan) = Fixture();
        plan.InternalPolicy = "None";
        Assert.Throws<ArgumentException>(() => Prepare(group, plan));
        plan.Positions[^1].Disposition = "Removed";
        plan.Positions[^1].SupportIndex = null;
        Assert.NotNull(Prepare(group, plan));
    }

    [Fact]
    public void VerticalPlanUsesRealSupportCoordinatesAndLeftDirection()
    {
        var (group, plan) = Fixture();
        plan.Side = "Left";
        plan.Positions = group.DimensionChains![DimensionChainSide.Left].Positions.Select((p, i) =>
            new DimensionPositionDecision { PositionIndex = i, Disposition = "Kept", Reason = "locate vertically",
                SupportIndex = p.Supports.ToList().FindIndex(s => s.ModelId.HasValue) }).ToList();
        var prepared = Prepare(group, plan);
        Assert.Equal("vertical-left", prepared.Direction);
        Assert.Equal(new[] { 0d, 90d, 250d, 340d }, prepared.Points.Where((_, i) => i % 3 == 1));
    }
}
