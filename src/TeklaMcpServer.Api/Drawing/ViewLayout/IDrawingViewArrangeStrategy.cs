using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public interface IDrawingViewArrangeStrategy
{
    bool CanArrange(DrawingArrangeContext context);
    bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames);
    List<ArrangedView> Arrange(DrawingArrangeContext context);
}

