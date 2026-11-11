using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>Midpoint of a section's line on its owner view, in sheet coordinates.</summary>
internal sealed class ConstraintLayoutCaseAnchor
{
    public int ViewId { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    /// <summary>The view that carries the section mark. Null in cases saved before this was recorded.</summary>
    public int? OwnerViewId { get; set; }

    /// <summary>
    /// The same midpoint in the owner view's own coordinates, model millimeters, as
    /// the view draws it: in a shortened view, with the stretches it leaves out taken
    /// off. It lands on the sheet at <c>owner view origin + local / scale</c>, so it
    /// stays valid when the owner view is moved. After a rescale of a shortened view
    /// it is off by the change in the paper gaps at the breaks.
    /// </summary>
    public double? OwnerLocalX { get; set; }

    /// <inheritdoc cref="OwnerLocalX"/>
    public double? OwnerLocalY { get; set; }
}

/// <summary>
/// How far a view's frame center is from the point where the model origin lands on
/// the sheet in that view. It does not change when the view is moved. Two views
/// are projection-aligned along an axis when the model origin lands on the same
/// sheet coordinate in both, so the difference of two anchors is the frame-center
/// offset that alignment needs.
/// </summary>
internal sealed class ConstraintLayoutCaseModelAnchor
{
    public int ViewId { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// Builds the anchor from a view's coordinate system, given in global model
    /// coordinates. A model point lands on the sheet at
    /// <c>view origin + (point in view coordinates) / scale</c>.
    /// Null when the scale or an axis is degenerate.
    /// </summary>
    public static ConstraintLayoutCaseModelAnchor? FromViewCoordinateSystem(
        int viewId,
        ReservedRect frame,
        double viewOriginX,
        double viewOriginY,
        double scale,
        (double X, double Y, double Z) coordinateSystemOrigin,
        (double X, double Y, double Z) axisX,
        (double X, double Y, double Z) axisY)
    {
        var lengthX = Math.Sqrt((axisX.X * axisX.X) + (axisX.Y * axisX.Y) + (axisX.Z * axisX.Z));
        var lengthY = Math.Sqrt((axisY.X * axisY.X) + (axisY.Y * axisY.Y) + (axisY.Z * axisY.Z));
        if (scale <= 0 || lengthX <= 1e-9 || lengthY <= 1e-9)
            return null;

        // The model origin in view coordinates: -(coordinate system origin) along each axis.
        var localX = -((coordinateSystemOrigin.X * axisX.X) + (coordinateSystemOrigin.Y * axisX.Y) + (coordinateSystemOrigin.Z * axisX.Z)) / lengthX;
        var localY = -((coordinateSystemOrigin.X * axisY.X) + (coordinateSystemOrigin.Y * axisY.Y) + (coordinateSystemOrigin.Z * axisY.Z)) / lengthY;

        return new ConstraintLayoutCaseModelAnchor
        {
            ViewId = viewId,
            X = ((frame.MinX + frame.MaxX) / 2.0) - (viewOriginX + (localX / scale)),
            Y = ((frame.MinY + frame.MaxY) / 2.0) - (viewOriginY + (localY / scale))
        };
    }
}

/// <summary>
/// How much of the model a view draws along its own axes, in model millimeters:
/// for a shortened view, the stretches it keeps, without those it leaves out.
/// Over the scale this is the part of the frame that changes with the scale; the
/// rest of the frame -- marks, dimensions, the gaps at the breaks -- does not.
/// </summary>
internal sealed class ConstraintLayoutCaseModelExtent
{
    public int ViewId { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

/// <summary>The side of the base view a view belongs on by the planner's own rules.</summary>
internal sealed class ConstraintLayoutCaseSide
{
    public int ViewId { get; set; }

    /// <summary>A <see cref="SectionPlacementSide"/> name.</summary>
    public string Side { get; set; } = string.Empty;
}

/// <summary>
/// Everything the constraint planner needs from one fit_views_to_sheet run, with no
/// Tekla objects, so the run can be replayed later without Tekla.
/// </summary>
internal sealed class ConstraintLayoutCase
{
    /// <summary>The candidate the current layout path selected: sizes, sides, sheet, reserved areas.</summary>
    public DrawingLayoutCandidate Reference { get; set; } = new();

    public int BaseViewId { get; set; }

    public double Gap { get; set; }

    /// <summary>
    /// One entry per view that has a section mark on another view. Being listed
    /// here is what makes a view a section for the planner, whatever its Tekla type.
    /// </summary>
    public List<ConstraintLayoutCaseAnchor> SectionLineAnchors { get; set; } = new();

    /// <summary>One entry per detail view whose mark was found: where the mark is on its owner view.</summary>
    public List<ConstraintLayoutCaseAnchor> DetailMarkAnchors { get; set; } = new();

    /// <summary>
    /// Per view; a view without one, or a case without the base view's, is aligned
    /// by frame centers.
    /// </summary>
    public List<ConstraintLayoutCaseModelAnchor> ModelAnchors { get; set; } = new();

    /// <summary>Per view that could be read; a view without one has its frame scaled proportionally.</summary>
    public List<ConstraintLayoutCaseModelExtent> ModelExtents { get; set; } = new();

    /// <summary>
    /// Sides by the planner's rules: a section or an end view where its arrows
    /// point, another projection where the topology puts it. They take precedence
    /// over the side the current path recorded on the reference view.
    /// </summary>
    public List<ConstraintLayoutCaseSide> ViewSides { get; set; } = new();

    /// <summary>
    /// What projection alignment the current path ran on the reference:
    /// "assembly", "ga", "single-part", or a "disabled-..." / "none" mode when it
    /// ran none. Only an aligned reference can be compared with the model anchors.
    /// </summary>
    public string ReferenceProjectionMode { get; set; } = string.Empty;
}

/// <summary>Stores layout cases as one JSON file per drawing.</summary>
internal static class ConstraintLayoutCaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Where fit_views_to_sheet drops the case of every run, next to the logs.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "svMCP", "LayoutCases");

    /// <summary>Writes the case, replacing an earlier one of the same drawing. Returns the file path.</summary>
    public static string Save(string directory, ConstraintLayoutCase layoutCase)
    {
        if (layoutCase == null)
            throw new ArgumentNullException(nameof(layoutCase));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, GetFileName(layoutCase.Reference.Drawing));
        File.WriteAllText(path, JsonSerializer.Serialize(layoutCase, JsonOptions));
        return path;
    }

    /// <summary>A copy that shares nothing with the original.</summary>
    public static ConstraintLayoutCase Clone(ConstraintLayoutCase layoutCase)
        => JsonSerializer.Deserialize<ConstraintLayoutCase>(JsonSerializer.Serialize(layoutCase, JsonOptions), JsonOptions)
            ?? throw new InvalidOperationException("Layout case could not be copied.");

    public static ConstraintLayoutCase Load(string path)
        => JsonSerializer.Deserialize<ConstraintLayoutCase>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Layout case '{path}' could not be read.");

    private static string GetFileName(DrawingInfo drawing)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string((drawing.Guid ?? string.Empty).Trim().Where(c => !invalid.Contains(c)).ToArray());
        return (name.Length == 0 ? "unknown" : name) + ".json";
    }
}
