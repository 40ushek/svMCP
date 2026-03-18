namespace TeklaMcpServer.Api.Drawing;

// Phase 1 keeps dimensions read-only.
// Future arrange_dimensions / overlap-resolution logic should live in this partial.
public sealed partial class TeklaDrawingDimensionsApi
{
    internal List<DimensionGroupSpacingAnalysis> AnalyzeDimensionGroupSpacing(int? viewId)
    {
        return GetDimensionGroups(viewId)
            .Select(DimensionGroupSpacingAnalyzer.Analyze)
            .ToList();
    }
}
