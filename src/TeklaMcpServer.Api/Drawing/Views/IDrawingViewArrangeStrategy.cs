using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingViewArrangeStrategy
{
    bool CanArrange(DrawingArrangeContext context);
    bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames);
    List<ArrangedView> Arrange(DrawingArrangeContext context);
}
