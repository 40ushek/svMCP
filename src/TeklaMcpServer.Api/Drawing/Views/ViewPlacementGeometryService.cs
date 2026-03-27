using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class ViewPlacementGeometryService
{
    public static ReservedRect CreateCandidateRect(
        View view,
        double originX,
        double originY,
        double width,
        double height)
    {
        if (DrawingViewFrameGeometry.TryGetBoundingRectAtOrigin(view, originX, originY, width, height, out var rect))
            return rect;

        return CreateCenteredRect(originX, originY, width, height);
    }

    public static ReservedRect CreateCenteredRect(
        double centerX,
        double centerY,
        double width,
        double height)
        => new(
            centerX - width * 0.5,
            centerY - height * 0.5,
            centerX + width * 0.5,
            centerY + height * 0.5);

    public static ReservedRect FromProjectionRect(ProjectionRect rect)
        => new(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY);
}
