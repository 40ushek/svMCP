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
            return BaseLineMarkGeometryBuilder.Build(mark, model, viewId);

        if (string.Equals(mark.Placing?.GetType().Name, "AlongLinePlacing", StringComparison.Ordinal))
            return AlongLineMarkGeometryBuilder.Build(mark, model, viewId);

        return FallbackMarkGeometryBuilder.Build(mark);
    }
}
