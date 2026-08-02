namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Result of <c>add_dimension_points</c>.
///
/// The command MERGES extra points into an existing dimension set, keeping its style and offset.
/// Use it whenever points only need to be ADDED — it is cheaper and safer than rebuilding.
///
/// Note the merged set is RENUMBERED by Tekla: read <see cref="MergedDimensionId"/> afterwards,
/// because <see cref="DimensionId"/> no longer addresses anything.
/// </summary>
public sealed class AddDimensionPointsResult
{
    public bool    Added           { get; set; }
    /// <summary>Id the caller passed in. Stale once the merge succeeds.</summary>
    public int     DimensionId     { get; set; }
    /// <summary>
    /// Id of the merged set — this is what later calls must use. Differs from
    /// <see cref="DimensionId"/> because Tekla renumbers the set when it absorbs another one.
    /// Zero when nothing was merged.
    /// </summary>
    public int     MergedDimensionId { get; set; }
    /// <summary>How many points were passed in for merging.</summary>
    public int     AddedPointCount { get; set; }
    /// <summary>Point count of the target set after the merge, re-read from Tekla.</summary>
    public int     PointCountAfter { get; set; }
    public string? Error           { get; set; }
}

/// <summary>
/// Result of <c>recreate_dimension</c>.
///
/// The command DESTROYS the dimension set and builds a new one from the given point list,
/// carrying over the original style attributes and offset. Tekla Open API cannot remove a
/// point from an existing set (<c>StraightDimensionSet</c> exposes no point collection), so
/// deleting and rebuilding is the only way to drop points.
///
/// The important consequence for callers: <see cref="NewDimensionId"/> differs from
/// <see cref="OldDimensionId"/>. Any id captured before the call is stale afterwards.
/// </summary>
public sealed class RecreateDimensionResult
{
    public bool    Recreated       { get; set; }
    /// <summary>Id of the set that was deleted. No longer addressable after the call.</summary>
    public int     OldDimensionId  { get; set; }
    /// <summary>Id of the freshly created set. This is what later calls must use.</summary>
    public int     NewDimensionId  { get; set; }
    public int     ViewId          { get; set; }
    public int     PointCount      { get; set; }
    /// <summary>False when the original attributes could not be read and defaults were used.</summary>
    public bool    AttributesKept  { get; set; }
    /// <summary>
    /// Offset the new set actually ended up with, re-read from a freshly fetched set after the
    /// correction was committed. When the correction failed this is the pre-correction value —
    /// it never reports an offset the sheet does not show.
    /// </summary>
    public double  Distance          { get; set; }
    /// <summary>Offset that was asked for — either the caller's value or the original's.</summary>
    public double  RequestedDistance { get; set; }
    /// <summary>
    /// How much the command had to shift the line after creating it, in mm. Creation does not
    /// honour the requested offset — attributes copied from the original carry their own, which
    /// Tekla adds on top — so the command assigns `Distance` on the created set afterwards, which
    /// is exact. That is NOT what `move_dimension` does: that one shifts `Distance` by a delta,
    /// this sets it to a value.
    ///
    /// A non-zero value is therefore normal and means the correction was applied. Compare
    /// <see cref="Distance"/> with <see cref="RequestedDistance"/> to see whether it succeeded: if
    /// they still differ, the correction did not take and the line needs attention.
    /// </summary>
    public double  DistanceCorrection { get; set; }
    /// <summary>
    /// Set when the offset correction failed, while the recreate itself succeeded. The new set
    /// exists and is addressable through <see cref="NewDimensionId"/>; only its line sits at the
    /// wrong offset and can be nudged with `move_dimension`. Distinct from <see cref="Error"/>,
    /// which means the recreate did not happen at all.
    /// </summary>
    public string? DistanceCorrectionError { get; set; }
    public string? Error             { get; set; }
}
