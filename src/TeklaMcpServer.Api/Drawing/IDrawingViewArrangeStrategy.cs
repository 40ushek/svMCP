using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingViewArrangeStrategy
{
    bool CanArrange(DrawingArrangeContext context);
    List<ArrangedView> Arrange(DrawingArrangeContext context);
}
