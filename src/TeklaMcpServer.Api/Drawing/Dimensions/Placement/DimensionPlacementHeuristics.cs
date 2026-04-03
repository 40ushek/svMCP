namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionPlacementHeuristics
{
    internal static double GetDimStyleAlongLineOffset(double viewScale)
        => viewScale <= 1e-6 ? 0.0 : viewScale / 4.0;

    internal static double GetAboveLineTextOffset(double textHeight, int sideSign)
    {
        if (textHeight <= 1e-6)
            return 0.0;

        var effectiveSideSign = sideSign == 0 ? 1 : sideSign;
        return (textHeight / 2.0) * effectiveSideSign;
    }

    internal static bool TryGetDimStyleLineVector(
        DrawingLineInfo dimensionLine,
        out (double X, double Y) lineVector)
    {
        lineVector = default;
        var useStartToEnd = !ComparePointsLeftToRight(
            (dimensionLine.StartX, dimensionLine.StartY),
            (dimensionLine.EndX, dimensionLine.EndY));
        var rawX = useStartToEnd
            ? dimensionLine.EndX - dimensionLine.StartX
            : dimensionLine.StartX - dimensionLine.EndX;
        var rawY = useStartToEnd
            ? dimensionLine.EndY - dimensionLine.StartY
            : dimensionLine.StartY - dimensionLine.EndY;
        return TeklaDrawingDimensionsApi.TryNormalizeDirection(rawX, rawY, out lineVector);
    }

    private static bool ComparePointsLeftToRight((double X, double Y) left, (double X, double Y) right)
    {
        if (!(left.X >= right.X && left.Y >= right.Y))
            return left.X > right.X && left.Y < right.Y;

        return true;
    }
}
