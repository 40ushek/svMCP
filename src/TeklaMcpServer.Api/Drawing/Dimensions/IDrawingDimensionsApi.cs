namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingDimensionsApi
{
    /// <summary>
    /// Returns all StraightDimensionSet objects from the active drawing (or a specific view).
    /// Each dimension set contains one or more segments with computed distances (mm).
    /// </summary>
    GetDimensionsResult GetDimensions(int? viewId);
    GetDimensionContextsResult GetDimensionContexts(int viewId);
    MoveDimensionResult MoveDimension(int dimensionId, double delta);
    MoveDimensionResult MoveAngleDimension(int dimensionId, double delta);
    DrawDimensionTextBoxesResult DrawDimensionTextBoxes(int? viewId, int? dimensionId, string color, string group);
    DrawAngleDimensionDebugGeometryResult DrawAngleDimensionDebugGeometry(int? viewId, int? dimensionId, string group);
    AngleDimensionDebugResult GetAngleDimensionDebug(int? viewId, int? dimensionId);
    CreateDimensionResult CreateDimension(int viewId, double[] points, string direction, double distance, string attributesFile);
    DeleteDimensionResult  DeleteDimension(int dimensionId);

    /// <summary>
    /// Merges extra points into an existing dimension set. Non-destructive: the set keeps its
    /// id, style and offset. Needs at least two points because a set cannot be built from one.
    /// </summary>
    AddDimensionPointsResult AddDimensionPoints(int dimensionId, double[] points, string direction);

    /// <summary>
    /// Rebuilds a dimension set from a new point list, carrying over style and offset.
    /// The set is deleted and recreated, so the id CHANGES — the caller must switch to
    /// NewDimensionId. Needed only when points must be REMOVED; Tekla Open API offers no way
    /// to drop a point from an existing set. To add points, use AddDimensionPoints instead.
    /// </summary>
    RecreateDimensionResult RecreateDimension(int dimensionId, double[] points, string direction, double? distance = null);
    CombineDimensionsResult CombineDimensions(int? viewId, IReadOnlyList<int>? dimensionIds, bool previewOnly);
    PlaceControlDiagonalsResult PlaceControlDiagonals(int? viewId, double distance, string attributesFile, int[] includeMaterialTypes);
    PlaceContourAngleDimensionsResult PlaceContourAngleDimensions(int? viewId, double distance, string attributesFile, bool skipRightAngles);
    PlaceContourRadiusDimensionsResult PlaceContourRadiusDimensions(int? viewId, double distance, string attributesFile);
    ArrangeDimensionsResult ArrangeDimensions(int? viewId, double targetGap, bool allowInwardCorrectionFromPartsBounds = false);
}
