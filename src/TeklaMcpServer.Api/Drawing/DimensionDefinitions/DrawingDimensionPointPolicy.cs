namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public sealed class DrawingDimensionPointPolicy
{
    public bool UseCharacteristicPoints { get; set; } = true;
    public bool UseExtremePoints { get; set; }
    public bool UseBoltPoints { get; set; }
    public bool UseWorkPoints { get; set; }
}
