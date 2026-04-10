using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

internal static class BaseLineMarkGeometryBuilder
{
    public static MarkGeometryInfo Build(Mark mark, Model model, int? viewId)
    {
        var bbox = mark.GetAxisAlignedBoundingBox();
        var objectAligned = mark.GetObjectAlignedBoundingBox();
        var centerX = (bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0;
        var centerY = (bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0;

        if (MarkPlacementAxisResolver.TryGetRelatedPartAxisInView(mark, model, viewId, out var partAxisDx, out var partAxisDy))
        {
            return MarkGeometryFactory.BuildFromAxis(
                centerX,
                centerY,
                objectAligned.Width,
                objectAligned.Height,
                partAxisDx,
                partAxisDy,
                mark.Attributes.Angle,
                "RelatedPartAxis",
                isReliable: true);
        }

        if (MarkPlacementAxisResolver.TryGetPlacingLineAxis(mark.Placing, out var placingAxisDx, out var placingAxisDy))
        {
            return MarkGeometryFactory.BuildFromAxis(
                centerX,
                centerY,
                objectAligned.Width,
                objectAligned.Height,
                placingAxisDx,
                placingAxisDy,
                mark.Attributes.Angle,
                "BaseLinePlacingAxisFallback",
                isReliable: true);
        }

        if (MarkPlacementAxisResolver.TryGetAngleAxis(mark.Attributes.Angle, out var angleDx, out var angleDy))
        {
            return MarkGeometryFactory.BuildFromAxis(
                centerX,
                centerY,
                objectAligned.Width,
                objectAligned.Height,
                angleDx,
                angleDy,
                mark.Attributes.Angle,
                "MarkAngleFallback",
                isReliable: false);
        }

        return FallbackMarkGeometryBuilder.Build(mark);
    }
}
