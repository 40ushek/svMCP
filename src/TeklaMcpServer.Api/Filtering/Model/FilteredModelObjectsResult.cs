using System.Collections.Generic;

namespace TeklaMcpServer.Api.Filtering;

public sealed class FilteredModelObjectsResult
{
    public string ObjectType { get; set; } = string.Empty;

    public int Count { get; set; }

    public bool SelectionApplied { get; set; }

    public List<int> ObjectIds { get; set; } = new();
}
