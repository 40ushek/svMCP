namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public interface IMarkDefinitionApi
{
    GetMarkDefinitionPresetResult GetDefaultPreset(DrawingMarkDefinitionScope scope);
}
