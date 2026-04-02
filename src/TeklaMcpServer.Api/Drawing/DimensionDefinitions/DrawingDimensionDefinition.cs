using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public sealed class DrawingDimensionDefinition
{
    public DrawingDimensionScenarioKind ScenarioKind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<DrawingDimensionSourceKind> Sources { get; set; } = new();
    public DrawingDimensionPlacementPolicy Placement { get; set; } = new();
    public DrawingDimensionPointPolicy Points { get; set; } = new();
}
