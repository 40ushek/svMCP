namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingDimensionsApi
{
    /// <summary>
    /// Returns all StraightDimensionSet objects from the active drawing (or a specific view).
    /// Each dimension set contains one or more segments with computed distances (mm).
    /// </summary>
    GetDimensionsResult GetDimensions(int? viewId);
    MoveDimensionResult MoveDimension(int dimensionId, double delta);
    DrawDimensionTextBoxesResult DrawDimensionTextBoxes(int? viewId, int? dimensionId, string color, string group);
    CreateDimensionResult CreateDimension(int viewId, double[] points, string direction, double distance, string attributesFile);
    DeleteDimensionResult  DeleteDimension(int dimensionId);
    CombineDimensionsResult CombineDimensions(int? viewId, IReadOnlyList<int>? dimensionIds, bool previewOnly);
    PlaceControlDiagonalsResult PlaceControlDiagonals(int? viewId, double distance, string attributesFile, int[] includeMaterialTypes);
    ArrangeDimensionsResult ArrangeDimensions(int? viewId, double targetGap, bool allowInwardCorrectionFromPartsBounds = false);
}
