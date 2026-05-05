using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutCandidateApplyPlanTests
{
    [Fact]
    public void FromEvaluation_BuildsMoves_ForPlannedCandidate()
    {
        var rect = new ReservedRect(0, 0, 30, 20);
        var evaluation = CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView
            {
                Id = 7,
                OriginX = 15,
                OriginY = 10,
                Scale = 20,
                LayoutRect = rect
            });

        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(evaluation);

        Assert.True(plan.CanApply);
        Assert.Equal(DrawingLayoutCandidateApplyPlanReason.PlannedCandidate, plan.Reason);
        Assert.Equal("fit_views_to_sheet:planned-centered", plan.CandidateName);
        var move = Assert.Single(plan.Moves);
        Assert.Equal(7, move.ViewId);
        Assert.Equal(15, move.TargetOriginX);
        Assert.Equal(10, move.TargetOriginY);
        Assert.Equal(20, move.Scale);
        Assert.Equal(rect, move.LayoutRect);
    }

    [Fact]
    public void FromEvaluation_DoesNotBuildMoves_ForRuntimeCandidate()
    {
        var evaluation = CreateEvaluation(
            "fit_views_to_sheet:final",
            new DrawingLayoutCandidateView
            {
                Id = 7,
                OriginX = 15,
                OriginY = 10,
                Scale = 20,
                LayoutRect = new ReservedRect(0, 0, 30, 20)
            });

        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(evaluation);

        Assert.False(plan.CanApply);
        Assert.Equal(DrawingLayoutCandidateApplyPlanReason.RuntimeCandidate, plan.Reason);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void FromEvaluation_ReportsMissingSelection()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(null);

        Assert.False(plan.CanApply);
        Assert.Equal(DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate, plan.Reason);
        Assert.Empty(plan.Moves);
    }

    [Theory]
    [InlineData(DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate, "no-selected-candidate")]
    [InlineData(DrawingLayoutCandidateApplyPlanReason.PlannedCandidate, "planned-candidate")]
    [InlineData(DrawingLayoutCandidateApplyPlanReason.RuntimeCandidate, "runtime-candidate")]
    public void ToTraceString_ReturnsStableTraceValue(
        DrawingLayoutCandidateApplyPlanReason reason,
        string expected)
    {
        Assert.Equal(expected, DrawingLayoutCandidateApplyPlanReasonFormatter.ToTraceString(reason));
    }

    [Fact]
    public void Execute_DryRun_ValidatesRuntimeViewsWithoutApplying()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));
        var applied = false;

        var result = new DrawingLayoutCandidateApplyService().Execute(
            plan,
            [7],
            DrawingLayoutCandidateApplyExecutionMode.DryRun,
            _ =>
            {
                applied = true;
                return true;
            });

        Assert.True(result.Success);
        Assert.False(applied);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionReason.DryRun, result.Reason);
        Assert.Equal(1, result.RequestedMoveCount);
        Assert.Equal(0, result.AppliedMoveCount);
    }

    [Fact]
    public void Execute_ReportsMissingRuntimeViews()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));

        var result = new DrawingLayoutCandidateApplyService().Execute(
            plan,
            [8],
            DrawingLayoutCandidateApplyExecutionMode.DryRun);

        Assert.False(result.Success);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView, result.Reason);
        Assert.Equal(1, result.MissingRuntimeViewCount);
        Assert.Equal(7, Assert.Single(result.MissingRuntimeViewIds));
    }

    [Fact]
    public void Execute_Apply_InvokesApplyHandler()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));
        DrawingLayoutCandidateApplyMove? appliedMove = null;

        var result = new DrawingLayoutCandidateApplyService().Execute(
            plan,
            [7],
            DrawingLayoutCandidateApplyExecutionMode.Apply,
            move =>
            {
                appliedMove = move;
                return true;
            });

        Assert.True(result.Success);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionReason.Applied, result.Reason);
        Assert.Equal(1, result.AppliedMoveCount);
        Assert.Equal(7, appliedMove?.ViewId);
    }

    [Theory]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.DryRun, "dry-run")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.Applied, "applied")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.PlanNotApplicable, "plan-not-applicable")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView, "missing-runtime-view")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.MissingApplyHandler, "missing-apply-handler")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.ApplyFailed, "apply-failed")]
    public void ExecutionReasonToTraceString_ReturnsStableTraceValue(
        DrawingLayoutCandidateApplyExecutionReason reason,
        string expected)
    {
        Assert.Equal(expected, DrawingLayoutCandidateApplyExecutionReasonFormatter.ToTraceString(reason));
    }

    [Theory]
    [InlineData(DrawingLayoutApplyMode.DebugPreview)]
    [InlineData(DrawingLayoutApplyMode.FinalOnly)]
    public void ApplyGate_DefaultsToDryRun(DrawingLayoutApplyMode applyMode)
    {
        DrawingLayoutCandidateApplyGate.EnableSelectedCandidateApply = false;

        Assert.Equal(
            DrawingLayoutCandidateApplyExecutionMode.DryRun,
            DrawingLayoutCandidateApplyGate.Resolve(applyMode));
    }

    [Fact]
    public void ApplyGate_AllowsApplyOnlyForFinalOnlyWhenEnabled()
    {
        try
        {
            DrawingLayoutCandidateApplyGate.EnableSelectedCandidateApply = true;

            Assert.Equal(
                DrawingLayoutCandidateApplyExecutionMode.DryRun,
                DrawingLayoutCandidateApplyGate.Resolve(DrawingLayoutApplyMode.DebugPreview));
            Assert.Equal(
                DrawingLayoutCandidateApplyExecutionMode.Apply,
                DrawingLayoutCandidateApplyGate.Resolve(DrawingLayoutApplyMode.FinalOnly));
        }
        finally
        {
            DrawingLayoutCandidateApplyGate.EnableSelectedCandidateApply = false;
        }
    }

    [Fact]
    public void BuildDeltas_ReportsZeroMovement_WhenPlanMatchesBaseline()
    {
        var baseline = CreateCandidate(
            "fit_views_to_sheet:final",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 });
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));

        var summary = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan);

        Assert.Equal(1, summary.MoveCount);
        Assert.Equal(1, summary.ComparableMoveCount);
        Assert.Equal(0, summary.MissingBaselineCount);
        Assert.Equal(0, summary.MovedCount);
        Assert.Equal(0, summary.ScaleChangedCount);
        Assert.Equal(0, summary.MaxDelta);
        Assert.Equal(0, summary.AverageDelta);
    }

    [Fact]
    public void BuildDeltas_UsesExplicitMovementThreshold()
    {
        var baseline = CreateCandidate(
            "fit_views_to_sheet:final",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 });
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView
            {
                Id = 7,
                OriginX = 15 + (DrawingLayoutCandidateApplyTolerances.Movement / 2.0),
                OriginY = 10,
                Scale = 20
            },
            new DrawingLayoutCandidateView
            {
                Id = 8,
                OriginX = 30 + DrawingLayoutCandidateApplyTolerances.Movement,
                OriginY = 10,
                Scale = 20
            }));
        baseline.Views.Add(new DrawingLayoutCandidateView { Id = 8, OriginX = 30, OriginY = 10, Scale = 20 });

        var summary = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan);

        Assert.Equal(1, summary.MovedCount);
        Assert.False(summary.Deltas.Single(delta => delta.ViewId == 7).Moved);
        Assert.True(summary.Deltas.Single(delta => delta.ViewId == 8).Moved);
    }

    [Fact]
    public void BuildDeltas_UsesSharedScaleTolerance()
    {
        var baseline = CreateCandidate(
            "fit_views_to_sheet:final",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 },
            new DrawingLayoutCandidateView { Id = 8, OriginX = 30, OriginY = 10, Scale = 20 });
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView
            {
                Id = 7,
                OriginX = 15,
                OriginY = 10,
                Scale = 20 + (DrawingLayoutCandidateApplyTolerances.Scale / 2.0)
            },
            new DrawingLayoutCandidateView
            {
                Id = 8,
                OriginX = 30,
                OriginY = 10,
                Scale = 20 + DrawingLayoutCandidateApplyTolerances.Scale
            }));

        var summary = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan);

        Assert.Equal(1, summary.ScaleChangedCount);
        Assert.False(summary.Deltas.Single(delta => delta.ViewId == 7).ScaleChanged);
        Assert.True(summary.Deltas.Single(delta => delta.ViewId == 8).ScaleChanged);
    }

    [Fact]
    public void BuildDeltas_ReportsMissingBaselineWithoutThrowing()
    {
        var baseline = CreateCandidate("fit_views_to_sheet:final");
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));

        var summary = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan);

        Assert.Equal(1, summary.MissingBaselineCount);
        Assert.True(Assert.Single(summary.Deltas).MissingBaseline);
    }

    [Fact]
    public void ApplySafetyPolicy_DefaultAllowsOriginOnlyApply()
    {
        var summary = BuildDeltaSummary(
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 },
            new DrawingLayoutCandidateView { Id = 7, OriginX = 25, OriginY = 10, Scale = 20 });

        var decision = DrawingLayoutCandidateApplySafetyPolicy.Default.Resolve(
            DrawingLayoutCandidateApplyExecutionMode.Apply,
            summary);

        Assert.True(decision.IsAllowed);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionMode.Apply, decision.EffectiveMode);
        Assert.Equal(DrawingLayoutCandidateApplySafetyDecisionReason.Allowed, decision.Reason);
    }

    [Fact]
    public void ApplySafetyPolicy_DefaultBlocksScaleChanges()
    {
        var summary = BuildDeltaSummary(
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 },
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 25 });

        var decision = DrawingLayoutCandidateApplySafetyPolicy.Default.Resolve(
            DrawingLayoutCandidateApplyExecutionMode.Apply,
            summary);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionMode.DryRun, decision.EffectiveMode);
        Assert.Equal(DrawingLayoutCandidateApplySafetyDecisionReason.ScaleChanged, decision.Reason);
    }

    [Fact]
    public void ApplySafetyPolicy_DefaultBlocksMissingBaseline()
    {
        var baseline = CreateCandidate("fit_views_to_sheet:final");
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));
        var summary = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan);

        var decision = DrawingLayoutCandidateApplySafetyPolicy.Default.Resolve(
            DrawingLayoutCandidateApplyExecutionMode.Apply,
            summary);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionMode.DryRun, decision.EffectiveMode);
        Assert.Equal(DrawingLayoutCandidateApplySafetyDecisionReason.MissingBaseline, decision.Reason);
    }

    [Fact]
    public void ApplySafetyPolicy_DryRunRequestRemainsDryRun()
    {
        var summary = BuildDeltaSummary(
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 },
            new DrawingLayoutCandidateView { Id = 7, OriginX = 25, OriginY = 10, Scale = 20 });

        var decision = DrawingLayoutCandidateApplySafetyPolicy.Default.Resolve(
            DrawingLayoutCandidateApplyExecutionMode.DryRun,
            summary);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionMode.DryRun, decision.EffectiveMode);
        Assert.Equal(DrawingLayoutCandidateApplySafetyDecisionReason.NotRequested, decision.Reason);
    }

    [Fact]
    public void ApplySafetyPolicy_BlocksWhenMaxDeltaExceeded()
    {
        var summary = BuildDeltaSummary(
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 },
            new DrawingLayoutCandidateView { Id = 7, OriginX = 25, OriginY = 10, Scale = 20 });
        var policy = new DrawingLayoutCandidateApplySafetyPolicy
        {
            MaxDelta = 5
        };

        var decision = policy.Resolve(
            DrawingLayoutCandidateApplyExecutionMode.Apply,
            summary);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionMode.DryRun, decision.EffectiveMode);
        Assert.Equal(DrawingLayoutCandidateApplySafetyDecisionReason.ExceedsMaxDelta, decision.Reason);
    }

    [Theory]
    [InlineData(DrawingLayoutCandidateApplySafetyDecisionReason.NotRequested, "not-requested")]
    [InlineData(DrawingLayoutCandidateApplySafetyDecisionReason.Allowed, "allowed")]
    [InlineData(DrawingLayoutCandidateApplySafetyDecisionReason.MissingBaseline, "missing-baseline")]
    [InlineData(DrawingLayoutCandidateApplySafetyDecisionReason.ScaleChanged, "scale-changed")]
    [InlineData(DrawingLayoutCandidateApplySafetyDecisionReason.ExceedsMaxDelta, "exceeds-max-delta")]
    public void SafetyDecisionReasonToTraceString_ReturnsStableTraceValue(
        DrawingLayoutCandidateApplySafetyDecisionReason reason,
        string expected)
    {
        Assert.Equal(expected, DrawingLayoutCandidateApplySafetyDecisionReasonFormatter.ToTraceString(reason));
    }

    private static DrawingLayoutCandidateEvaluation CreateEvaluation(
        string candidateName,
        params DrawingLayoutCandidateView[] views)
        => new()
        {
            Candidate = new DrawingLayoutCandidate
            {
                Name = candidateName,
                Views = views.ToList()
            }
        };

    private static DrawingLayoutCandidate CreateCandidate(
        string candidateName,
        params DrawingLayoutCandidateView[] views)
        => new()
        {
            Name = candidateName,
            Views = views.ToList()
        };

    private static DrawingLayoutCandidateApplyDeltaSummary BuildDeltaSummary(
        DrawingLayoutCandidateView baselineView,
        DrawingLayoutCandidateView targetView)
    {
        var baseline = CreateCandidate("fit_views_to_sheet:final", baselineView);
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            targetView));
        return DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan);
    }
}
