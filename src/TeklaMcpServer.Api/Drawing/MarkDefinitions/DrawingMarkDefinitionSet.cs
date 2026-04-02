using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public sealed class DrawingMarkDefinitionSet
{
    public DrawingMarkDefinitionScope Scope { get; set; }
    public List<DrawingMarkDefinition> Definitions { get; set; } = new();
}
