namespace TeklaMcpServer.Api.Drawing;

public sealed class AssemblyGeometry
{
    public int ModelId { get; set; }
    public string? AssemblyType { get; set; }
    public int MainPartId { get; set; }
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
    public List<int> SubAssemblyIds { get; set; } = new();
    public List<AssemblyPartGeometry> PartMembers { get; set; } = new();
    public List<BoltGroupGeometry> BoltGroups { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
