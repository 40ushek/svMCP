using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingViewArrangementSelector
{
    private readonly IReadOnlyList<IDrawingViewArrangeStrategy> _strategies;

    public DrawingViewArrangementSelector(IReadOnlyList<IDrawingViewArrangeStrategy> strategies)
    {
        _strategies = strategies ?? throw new System.ArgumentNullException(nameof(strategies));
        if (_strategies.Count == 0)
            throw new System.ArgumentException("At least one arrangement strategy is required.", nameof(strategies));
    }

    public static DrawingViewArrangementSelector CreateDefault()
    {
        return new DrawingViewArrangementSelector(new IDrawingViewArrangeStrategy[]
        {
            new FrontViewDrawingArrangeStrategy(),
            new ShelfPackingDrawingArrangeStrategy()
        });
    }

    public List<ArrangedView> Arrange(DrawingArrangeContext context)
    {
        var strategy = _strategies.FirstOrDefault(s => s.CanArrange(context));
        if (strategy == null)
            throw new System.InvalidOperationException("No drawing view arrangement strategy matched the current drawing.");

        return strategy.Arrange(context);
    }
}
