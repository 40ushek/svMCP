using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class DrawingLayoutCandidateApplyGate
{
    public static bool EnableSelectedCandidateApply { get; set; }

    public static DrawingLayoutCandidateApplyExecutionMode Resolve(DrawingLayoutApplyMode applyMode)
    {
        if (!EnableSelectedCandidateApply)
            return DrawingLayoutCandidateApplyExecutionMode.DryRun;

        return applyMode == DrawingLayoutApplyMode.FinalOnly
            ? DrawingLayoutCandidateApplyExecutionMode.Apply
            : DrawingLayoutCandidateApplyExecutionMode.DryRun;
    }
}
