namespace TeklaMcpServer.Api.Drawing;

public sealed class AssemblyPartGeometry
{
    public int ModelId { get; set; }
    public int OwningAssemblyId { get; set; }
    public bool IsMainPart { get; set; }
    public bool IsDirectMember { get; set; }
    public string? Name { get; set; }
    public string? Profile { get; set; }
    public string? Material { get; set; }
    public string? PartPos { get; set; }
    public string? AssemblyPos { get; set; }
    public PartSolidGeometry Solid { get; set; } = new();
}
