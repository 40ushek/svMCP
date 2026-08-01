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
    /// <summary>Offset the new set actually ended up with, re-read from Tekla after correction.</summary>
    public double  Distance          { get; set; }
    /// <summary>Offset that was asked for — either the caller's value or the original's.</summary>
    public double  RequestedDistance { get; set; }
    /// <summary>
    /// <see cref="Distance"/> minus <see cref="RequestedDistance"/>, in mm. Non-zero means Tekla
    /// placed the dimension line somewhere other than asked, because attributes copied from the
    /// original carry their own offset that gets added on top.
    ///
    /// The command does NOT correct this — the relation between Distance and the actual line
    /// position depends on the dimension direction and on which extreme point Tekla measures
    /// from, and that convention is not pinned down yet. Check this value and nudge the line with
    /// move_dimension, which changes Distance in place and is exact.
    /// </summary>
    public double  DistanceCorrection { get; set; }
    public string? Error             { get; set; }
}
