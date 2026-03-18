namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingDimensionsApi
{
    /// <summary>
    /// Returns all StraightDimensionSet objects from the active drawing (or a specific view).
    /// Each dimension set contains one or more segments with computed distances (mm).
    /// </summary>
    GetDimensionsResult    GetDimensions(int? viewId);
    ArrangeDimensionsResult ArrangeDimensions(int? viewId, double targetGap);
    MoveDimensionResult    MoveDimension(int dimensionId, double delta);
    CreateDimensionResult  CreateDimension(int viewId, double[] points, string direction, double distance, string attributesFile);
    DeleteDimensionResult  DeleteDimension(int dimensionId);
    PlaceControlDiagonalsResult PlaceControlDiagonals(int? viewId, double distance, string attributesFile);
}
