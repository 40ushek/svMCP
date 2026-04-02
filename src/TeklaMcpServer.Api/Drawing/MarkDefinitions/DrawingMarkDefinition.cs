using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public sealed class DrawingMarkDefinition
{
    public DrawingMarkScenarioKind ScenarioKind { get; set; }
    public DrawingMarkTargetKind TargetKind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DrawingMarkPlacementPolicy Placement { get; set; } = new();
    public DrawingMarkContentPolicy Content { get; set; } = new();
    public DrawingMarkStylePolicy Style { get; set; } = new();
    public List<string> Filters { get; set; } = new();
}
