using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

// Compatibility facade. Canonical geometry resolution lives in MarkGeometryResolver.
// Keep this public wrapper so TeklaBridge (cross-assembly) can call Build() without
// accessing internal MarkGeometryResolver directly.
public static class MarkGeometryHelper
{
    public static MarkGeometryInfo Build(Mark mark, Model model, int? viewId = null)
    {
        return MarkGeometryResolver.Build(mark, model, viewId);
    }
}
