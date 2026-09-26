using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Placement intent, resolved separately from point selection. Future placement policies
/// (including interior lines) extend this contract without changing the rule executor.
/// </summary>
internal abstract class DimensionLinePlacement { }

/// <summary>The existing preview's outside-outline policy; no distance is resolved here.</summary>
internal sealed class OutsideOutlineDimensionPlacement : DimensionLinePlacement
{
    public DimensionDirection OutwardNormal { get; }
    public string Row { get; }

    public OutsideOutlineDimensionPlacement(DimensionDirection outwardNormal, string row)
    {
        OutwardNormal = outwardNormal ?? throw new ArgumentNullException(nameof(outwardNormal));
        if (string.IsNullOrWhiteSpace(row)) throw new ArgumentException("A placement row is required.", nameof(row));
        Row = row;
    }
}
