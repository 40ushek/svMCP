using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingViewArrangeStrategy
{
    bool CanArrange(DrawingArrangeContext context);
    bool EstimateFit(IReadOnlyList<(double w, double h)> frames, double availableWidth, double availableHeight, double gap);
    List<ArrangedView> Arrange(DrawingArrangeContext context);
}
