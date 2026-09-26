using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class TimberPanelPartLocationSettings
{
    public double MinimumSegmentViewUnits { get; }

    public TimberPanelPartLocationSettings(double minimumSegmentViewUnits)
    {
        if (double.IsNaN(minimumSegmentViewUnits) || double.IsInfinity(minimumSegmentViewUnits)
            || minimumSegmentViewUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentViewUnits));
        MinimumSegmentViewUnits = minimumSegmentViewUnits;
    }
}
