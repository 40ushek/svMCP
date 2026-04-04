namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionOrchestrationEngine
{
    public DimensionOrchestrationDebugResult BuildDebug(DimensionReductionDebugResult debug, int? viewId)
    {
        return DimensionOrchestrationDebugBuilder.Build(debug, viewId);
    }

    public DimensionAiOrchestrationPlanResult BuildPlan(DimensionReductionDebugResult debug, int? viewId)
    {
        return new DimensionAiAssistedOrchestrator().Build(debug, viewId);
    }
}
