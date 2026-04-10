using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

internal static class BaseLineMarkGeometryBuilder
{
    public static MarkGeometryInfo Build(Mark mark, Model model, int? viewId)
    {
        if (!MarkGeometryFactory.TryGetObjectAlignedBoundingBox(mark, out var box))
            return FallbackMarkGeometryBuilder.Build(mark);

        if (MarkPlacementAxisResolver.TryGetRelatedPartAxisInView(mark, model, viewId, out var partAxisDx, out var partAxisDy))
        {
            return MarkGeometryFactory.BuildFromObjectAlignedBoxAndAxis(
                box,
                partAxisDx,
                partAxisDy,
                "RelatedPartAxis",
                isReliable: true);
        }

        if (MarkPlacementAxisResolver.TryGetPlacingLineAxis(mark.Placing, out var placingAxisDx, out var placingAxisDy))
        {
            return MarkGeometryFactory.BuildFromObjectAlignedBoxAndAxis(
                box,
                placingAxisDx,
                placingAxisDy,
                "BaseLinePlacingAxisFallback",
                isReliable: true);
        }

        if (MarkPlacementAxisResolver.TryGetAngleAxis(mark.Attributes.Angle, out var angleDx, out var angleDy))
        {
            return MarkGeometryFactory.BuildFromObjectAlignedBoxAndAxis(
                box,
                angleDx,
                angleDy,
                "MarkAngleFallback",
                isReliable: false);
        }

        return FallbackMarkGeometryBuilder.Build(mark);
    }
}
