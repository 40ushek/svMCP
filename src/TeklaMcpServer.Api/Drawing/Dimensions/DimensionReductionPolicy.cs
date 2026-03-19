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
    public bool EnableCoverageReduction { get; set; } = true;
    public bool EnableRepresentativeSelection { get; set; } = true;
    public double RepresentativePacketGapFactor { get; set; } = 1.0;
    public DimensionRepresentativeSelectionMode RepresentativeSelectionMode { get; set; } =
        DimensionRepresentativeSelectionMode.CenterBiased;
}
