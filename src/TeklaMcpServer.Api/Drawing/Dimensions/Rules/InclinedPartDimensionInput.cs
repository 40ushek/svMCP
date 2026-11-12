using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Projected outer contours of the parts of a view, by model id, and the main part.</summary>
internal sealed class InclinedPartDimensionInput
{
    public IReadOnlyDictionary<int, IReadOnlyList<(double X, double Y)>> PartContours { get; }
    public int MainPartId { get; }

    public InclinedPartDimensionInput(IReadOnlyDictionary<int, IReadOnlyList<(double X, double Y)>> partContours,
        int mainPartId)
    {
        PartContours = partContours ?? throw new ArgumentNullException(nameof(partContours));
        MainPartId = mainPartId;
    }

    /// <summary>Parts drawn as exactly one outer polygon; anything else is left to other rules.</summary>
    public static InclinedPartDimensionInput FromGroup(GeometryGroup group, int mainPartId) => new(
        group.Shapes.Where(shape => shape.ModelId.HasValue && !shape.IsHole
                && shape.Shape.Kind == SolidContacts.PlanarShapeKind.Polygon)
            .GroupBy(shape => shape.ModelId!.Value).Where(part => part.Count() == 1)
            .ToDictionary(part => part.Key, part => (IReadOnlyList<(double X, double Y)>)part.Single().Shape.Points
                .Select(point => (point.X, point.Y)).ToArray()),
        mainPartId);
}
