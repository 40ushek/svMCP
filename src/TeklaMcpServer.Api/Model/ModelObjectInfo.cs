namespace TeklaMcpServer.Api.Model;

public sealed class ModelObjectInfo
{
    public int Id { get; set; }

    public string? Guid { get; set; }

    public string? Name { get; set; }

    public string? Profile { get; set; }

    public string? Material { get; set; }

    public string? Class { get; set; }

    public double? WeightKg { get; set; }
}
