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
