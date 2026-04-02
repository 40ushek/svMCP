using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class DrawingSectionDefinitionSet
{
    public DrawingSectionDefinitionScope Scope { get; set; }
    public System.Collections.Generic.List<DrawingSectionDefinition> Definitions { get; set; } = new();
}
