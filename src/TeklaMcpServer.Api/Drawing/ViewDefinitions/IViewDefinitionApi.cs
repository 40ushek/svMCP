namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public interface IViewDefinitionApi
{
    GetViewDefinitionPresetResult GetDefaultPreset(DrawingViewDefinitionScope scope);
}
