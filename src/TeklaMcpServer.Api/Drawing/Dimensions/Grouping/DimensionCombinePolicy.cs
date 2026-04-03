namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionCombinePolicy
{
    public static DimensionCombinePolicy Default => new();

    public bool AllowAdjacentMeasuredPointOrderFallback { get; set; } = true;
    public bool AllowFreeDimensionCombine { get; set; }
    public bool RequireSameSourceKind { get; set; }
    public bool RequireSameTopDirection { get; set; } = true;
    public bool RequireReferenceLineOffsetCompatibility { get; set; }
    public double ReferenceLineOffsetTolerance { get; set; } = 50.0;
    public bool RequireDistanceCompatibility { get; set; }
    public double DistanceTolerance { get; set; } = 50.0;
    public bool RequireCommonLeadLineFamily { get; set; }
    public double LeadLineCollinearityTolerance { get; set; } = 3.0;
    public bool UseRepresentativeAsPreviewBase { get; set; } = true;
}
