using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

// Public bridge entry point for TeklaBridge (net48), which cannot access internal
// MarkGeometryResolver directly. All code within TeklaMcpServer.Api should call
// MarkGeometryResolver.Build() instead.
public static class MarkGeometryHelper
{
    public static MarkGeometryInfo Build(Mark mark, Model model, int? viewId = null)
    {
        return MarkGeometryResolver.Build(mark, model, viewId);
    }
}
