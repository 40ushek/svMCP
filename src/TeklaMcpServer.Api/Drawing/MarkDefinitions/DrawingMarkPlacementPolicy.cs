namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public sealed class DrawingMarkPlacementPolicy
{
    public DrawingMarkPlacementMode PreferredMode { get; set; } = DrawingMarkPlacementMode.Auto;
    public bool PreferOutsideContour { get; set; }
    public bool AllowLeaderLine { get; set; } = true;
    public bool AllowInsidePlacement { get; set; } = true;
}
