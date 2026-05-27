using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkLayoutFixedBlockerBuilder
{
    internal static List<IReadOnlyList<double[]>> BuildDimensionTextBoxPolygons(DrawingViewContext viewContext)
    {
        return viewContext.DimensionTextBoxes
            .Where(static textBox => textBox.Polygon.Count >= 3)
            .Select(static textBox => (IReadOnlyList<double[]>)textBox.Polygon)
            .ToList();
    }
}
