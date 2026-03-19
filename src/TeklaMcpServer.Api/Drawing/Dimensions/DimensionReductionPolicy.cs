namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionRepresentativeSelectionMode
{
    CenterBiased = 0,
    FirstInPacket,
    LastInPacket
}

internal sealed class DimensionReductionPolicy
{
    public static DimensionReductionPolicy Default => new();

    public double PositionTolerance { get; set; } = 3.0;
    public double LengthTolerance { get; set; } = 3.0;
    public double MeasuredLineTolerance { get; set; } = 3.0;
    public bool EnableEquivalentSimpleReduction { get; set; } = true;
    public bool EnableCoverageReduction { get; set; } = true;
    public bool EnableRepresentativeSelection { get; set; } = true;
    public bool AllowAdjacentMeasuredPointOrderCombineFallback { get; set; } = true;
    public double RepresentativePacketGapFactor { get; set; } = 1.0;
    public bool UseGeometryAwareRepresentativeSelection { get; set; }
    public DimensionRepresentativeSelectionMode RepresentativeSelectionMode { get; set; } =
        DimensionRepresentativeSelectionMode.CenterBiased;
    public DimensionRepresentativeSelectionMode HorizontalRepresentativeSelectionMode { get; set; } =
        DimensionRepresentativeSelectionMode.CenterBiased;
    public DimensionRepresentativeSelectionMode VerticalRepresentativeSelectionMode { get; set; } =
        DimensionRepresentativeSelectionMode.CenterBiased;
    public DimensionRepresentativeSelectionMode FreeRepresentativeSelectionMode { get; set; } =
        DimensionRepresentativeSelectionMode.CenterBiased;
}
