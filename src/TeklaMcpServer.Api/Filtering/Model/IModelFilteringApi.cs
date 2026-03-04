namespace TeklaMcpServer.Api.Filtering;

public interface IModelFilteringApi
{
    FilteredModelObjectsResult FilterByType(ModelObjectFilter filter);
}
