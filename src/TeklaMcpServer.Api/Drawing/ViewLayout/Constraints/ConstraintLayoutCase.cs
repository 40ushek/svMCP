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
/// Everything the constraint planner needs from one fit_views_to_sheet run, with no
/// Tekla objects, so the run can be replayed later without Tekla.
/// </summary>
internal sealed class ConstraintLayoutCase
{
    /// <summary>The candidate the current layout path selected: sizes, sides, sheet, reserved areas.</summary>
    public DrawingLayoutCandidate Reference { get; set; } = new();

    public int BaseViewId { get; set; }

    public double Gap { get; set; }

    public List<ConstraintLayoutCaseAnchor> SectionLineAnchors { get; set; } = new();

    /// <summary>
    /// Per view; a view without one, or a case without the base view's, is aligned
    /// by frame centers.
    /// </summary>
    public List<ConstraintLayoutCaseModelAnchor> ModelAnchors { get; set; } = new();
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
