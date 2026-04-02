using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public sealed class DrawingDimensionDefinitionSet
{
    public DrawingDimensionDefinitionScope Scope { get; set; }
    public List<DrawingDimensionDefinition> Definitions { get; set; } = new();
}
