namespace TeklaMcpServer.Api.Filtering;

public sealed class ModelObjectFilter
{
    public string ObjectType { get; set; } = string.Empty;

    public bool SelectMatches { get; set; } = true;
}
