namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroupingPolicy
{
    public static DimensionGroupingPolicy Default => new();

    public double ParallelDotTolerance { get; set; } = 0.995;
    public double ExtentOverlapTolerance { get; set; } = 3.0;
    public double LineCollinearityTolerance { get; set; } = 3.0;
    public double LineBandTolerance { get; set; } = 3.0;
    public double ChainBandTolerance { get; set; } = 250.0;
    public double ChainExtentGapTolerance { get; set; } = 80.0;
    public double SharedPointTolerance { get; set; } = 0.5;
}
