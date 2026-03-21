using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingViewArrangeDiagnosticsStrategy
{
    List<DrawingFitConflict> DiagnoseFitConflicts(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames);
}
