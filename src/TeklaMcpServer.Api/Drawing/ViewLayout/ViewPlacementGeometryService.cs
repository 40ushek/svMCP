using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class ViewPlacementGeometryService
{
    public static ReservedRect CreateRectFromOrigin(
        DrawingArrangeContext context,
        View view,
        double originX,
        double originY,
        double width,
        double height)
    {
        var (offsetX, offsetY) = GetFrameOffsetSheet(context, view);
        return CreateCenteredRect(originX + offsetX, originY + offsetY, width, height);
    }

    public static ReservedRect CreateRectFromOrigin(
        DrawingLayoutWorkspace workspace,
        View view,
        double originX,
        double originY,
        double width,
        double height)
    {
        var (offsetX, offsetY) = GetFrameOffsetSheet(workspace, view);
        return CreateCenteredRect(originX + offsetX, originY + offsetY, width, height);
    }

    public static ReservedRect CreateRectFromOrigin(
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

    public static ReservedRect CreateRectFromFrameCenter(
        double frameCenterX,
        double frameCenterY,
        double width,
        double height)
        => CreateCenteredRect(frameCenterX, frameCenterY, width, height);

    public static (double X, double Y) GetFrameOffsetSheet(DrawingArrangeContext context, View view)
    {
        var id = view.GetIdentifier().ID;
        if (context.Workspace?.FrameOffsetsById.TryGetValue(id, out var storedOffset) == true)
        {
            var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
            return (storedOffset.X / scale, storedOffset.Y / scale);
        }

        return DrawingViewFrameGeometry.TryGetCenterOffsetFromOrigin(view, out var offsetX, out var offsetY)
            ? (offsetX, offsetY)
            : (0.0, 0.0);
    }

    public static (double X, double Y) GetFrameOffsetSheet(DrawingLayoutWorkspace workspace, View view)
    {
        var id = view.GetIdentifier().ID;
        if (workspace.FrameOffsetsById.TryGetValue(id, out var storedOffset))
        {
            var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
            return (storedOffset.X / scale, storedOffset.Y / scale);
        }

        return DrawingViewFrameGeometry.TryGetCenterOffsetFromOrigin(view, out var offsetX, out var offsetY)
            ? (offsetX, offsetY)
            : (0.0, 0.0);
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

