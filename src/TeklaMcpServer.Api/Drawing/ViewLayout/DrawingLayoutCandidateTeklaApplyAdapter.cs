using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class DrawingLayoutCandidateTeklaApplyAdapter
{
    private const double ScaleTolerance = 0.01;

    private readonly DrawingLayoutCandidateApplyService applyService;

    public DrawingLayoutCandidateTeklaApplyAdapter()
        : this(new DrawingLayoutCandidateApplyService())
    {
    }

    public DrawingLayoutCandidateTeklaApplyAdapter(DrawingLayoutCandidateApplyService applyService)
    {
        this.applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
    }

    public DrawingLayoutCandidateApplyExecutionResult Execute(
        DrawingLayoutCandidateApplyPlan plan,
        IReadOnlyDictionary<int, View> runtimeViewsById,
        DrawingLayoutCandidateApplyExecutionMode mode)
    {
        if (runtimeViewsById == null)
            throw new ArgumentNullException(nameof(runtimeViewsById));

        return applyService.Execute(
            plan,
            runtimeViewsById.Keys.ToList(),
            mode,
            mode == DrawingLayoutCandidateApplyExecutionMode.Apply
                ? move => ApplyMove(runtimeViewsById[move.ViewId], move)
                : null);
    }

    private static bool ApplyMove(View view, DrawingLayoutCandidateApplyMove move)
    {
        if (view == null)
            return false;

        var origin = view.Origin ?? new Point();
        origin.X = move.TargetOriginX;
        origin.Y = move.TargetOriginY;
        view.Origin = origin;

        if (move.Scale > 0 && Math.Abs(view.Attributes.Scale - move.Scale) >= ScaleTolerance)
            view.Attributes.Scale = move.Scale;

        return view.Modify();
    }
}
