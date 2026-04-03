using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionDiagonalPlacementHelper
{
    internal static string NormalizeAttributesFile(string? attributesFile)
        => string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile!.Trim();

    internal static StraightDimensionSet.StraightDimensionSetAttributes CreateAttributes(string? attributesFile)
    {
#pragma warning disable CS0618
        var attributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
        attributes.LoadAttributes(NormalizeAttributesFile(attributesFile));
        return attributes;
    }

    internal static Vector BuildOffsetDirection(Point start, Point end)
        => DimensionProjectionHelper.BuildPerpendicularOffsetDirection(start, end);

    internal static double ResolveDistance(double distance, int diagonalIndex, bool diagonalsIntersect)
        => diagonalIndex == 1 && diagonalsIntersect ? distance * 2.0 : distance;

    internal static (Point Start, Point End) NormalizeBottomToTop((Point Start, Point End) pair)
        => pair.Start.Y > pair.End.Y ? (pair.End, pair.Start) : pair;
}
