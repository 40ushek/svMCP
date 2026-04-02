namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public interface IDimensionDefinitionApi
{
    GetDimensionDefinitionPresetResult GetDefaultPreset(DrawingDimensionDefinitionScope scope);
}
