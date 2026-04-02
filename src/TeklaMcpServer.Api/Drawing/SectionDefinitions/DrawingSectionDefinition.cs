namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class DrawingSectionDefinition
{
    public DrawingSectionScenarioKind ScenarioKind { get; set; }
    public bool IsEnabled { get; set; }
    public bool ExtendByAlongAxis { get; set; }
    public DrawingSectionSymbolDirection SymbolDirection { get; set; } = DrawingSectionSymbolDirection.Auto;
    public DrawingSectionNamingPolicy Naming { get; set; } = new();
    public DrawingSectionMergePolicy Merge { get; set; } = new();
    public DrawingSectionStylePolicy Style { get; set; } = new();
}
