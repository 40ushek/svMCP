namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public interface ISectionDefinitionApi
{
    GetSectionDefinitionPresetResult GetDefaultPreset(DrawingSectionDefinitionScope scope);
}
