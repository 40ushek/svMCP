using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkGeometryResolver
{
    public static MarkGeometryInfo Build(Mark mark, Model model, int? viewId = null)
    {
        if (mark.Placing is LeaderLinePlacing)
            return LeaderLineMarkGeometryBuilder.Build(mark);

        if (mark.Placing is BaseLinePlacing)
            return AxisAlignedMarkGeometryBuilder.Build(mark, model, viewId, "BaseLinePlacingAxisFallback");

        if (string.Equals(mark.Placing?.GetType().Name, "AlongLinePlacing", StringComparison.Ordinal))
            return AxisAlignedMarkGeometryBuilder.Build(mark, model, viewId, "AlongLinePlacingAxisFallback");

        return FallbackMarkGeometryBuilder.Build(mark);
    }
}
