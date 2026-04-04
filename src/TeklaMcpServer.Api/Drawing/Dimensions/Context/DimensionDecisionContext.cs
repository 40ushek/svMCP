using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionDecisionContext
{
    public List<DimensionContext> Dimensions { get; } = [];
    public DimensionViewContext View { get; set; } = new();
    public List<string> Warnings { get; } = [];

    public bool HasDimensions => Dimensions.Count > 0;
    public bool IsEmpty => Dimensions.Count == 0 && View.IsEmpty;

    public DimensionContext? FindDimension(int dimensionId) =>
        Dimensions.FirstOrDefault(context => context.DimensionId == dimensionId);
}
