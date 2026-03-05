namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingDimensionsApi
{
    /// <summary>
    /// Returns all StraightDimensionSet objects from the active drawing (or a specific view).
    /// Each dimension set contains one or more segments with computed distances (mm).
    /// </summary>
    GetDimensionsResult   GetDimensions(int? viewId);
    MoveDimensionResult   MoveDimension(int dimensionId, double delta);
}
