using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Shared;

/// <summary>Common compact/batch interpretation of factual dimension geometry.</summary>
internal sealed class DimensionReadbackGeometry
{
    internal string Side { get; private set; } = "Unknown";
    internal double? LineAt { get; private set; }
    internal double? From { get; private set; }
    internal double? To { get; private set; }
    internal double[] Segments { get; private set; } = [];
    internal const string LineAtSource = "calculated; not a presentation observation";

    internal static DimensionReadbackGeometry Read(string? type, int topDirection,
        IReadOnlyList<(double X, double Y)> points, double? lineX, double? lineY, double[]? lengthsFromFirstPoint)
    {
        // Tekla's LengthList holds each point's distance from the first point, not the steps
        // between neighbours: a straight chain is read from its points, any other one from the
        // differences of those distances.
        var lengths = (lengthsFromFirstPoint ?? []).OrderBy(value => value).ToArray();
        var fromLengths = lengths.Select((value, index) => Math.Round(index == 0 ? value : value - lengths[index - 1], 3)).ToArray();
        var horizontal = string.Equals(type, "Horizontal", StringComparison.OrdinalIgnoreCase);
        var vertical = string.Equals(type, "Vertical", StringComparison.OrdinalIgnoreCase);
        var axis = points.Select(point => horizontal ? point.X : point.Y).OrderBy(value => value).ToArray();
        // Read by a model: three decimals, so rounding noise does not look like a different line.
        static double? Round(double? value) => value.HasValue ? Math.Round(value.Value, 3) : null;
        return new DimensionReadbackGeometry {
            Side = horizontal ? (topDirection >= 0 ? "Top" : "Bottom")
                : vertical ? (topDirection >= 0 ? "Left" : "Right") : "Unknown",
            LineAt = Round(horizontal ? lineY : vertical ? lineX : null),
            From = axis.Length > 0 && (horizontal || vertical) ? Round(axis[0]) : null,
            To = axis.Length > 0 && (horizontal || vertical) ? Round(axis[axis.Length - 1]) : null,
            Segments = horizontal || vertical
                ? axis.Zip(axis.Skip(1), (a, b) => Math.Round(b - a, 3)).ToArray() : fromLengths
        };
    }
}
