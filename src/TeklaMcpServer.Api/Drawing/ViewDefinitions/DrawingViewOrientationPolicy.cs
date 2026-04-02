namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class DrawingViewOrientationPolicy
{
    public DrawingViewCoordinateSystemSource CoordinateSystemSource { get; set; } = DrawingViewCoordinateSystemSource.Auto;
    public DrawingViewAxisRotation AxisRotationX { get; set; } = DrawingViewAxisRotation.Auto;
    public DrawingViewAxisRotation AxisRotationY { get; set; } = DrawingViewAxisRotation.Auto;
}
