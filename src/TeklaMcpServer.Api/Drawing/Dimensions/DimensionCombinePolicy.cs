namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionCombinePolicy
{
    public static DimensionCombinePolicy Default => new();

    public bool AllowAdjacentMeasuredPointOrderFallback { get; set; } = true;
    public bool AllowFreeDimensionCombine { get; set; }
    public bool RequireSameSourceKind { get; set; }
}
