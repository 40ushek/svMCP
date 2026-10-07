using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class TimberPanelPartLocationSettings
{
    public double MinimumSegmentViewUnits { get; }
    public IReadOnlyCollection<string> ReferenceZones { get; }

    public TimberPanelPartLocationSettings(double minimumSegmentViewUnits, IReadOnlyCollection<string>? referenceZones = null)
    {
        if (double.IsNaN(minimumSegmentViewUnits) || double.IsInfinity(minimumSegmentViewUnits)
            || minimumSegmentViewUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentViewUnits));
        MinimumSegmentViewUnits = minimumSegmentViewUnits;
        ReferenceZones = Array.AsReadOnly(ReferenceZoneOutline.Normalize(referenceZones));
    }
}
