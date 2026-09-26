using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Verified section-profile levels captured before rule execution.</summary>
internal sealed class SectionProfileDimensionInput
{
    public int MainPartId { get; }
    public double[] XLevels { get; }
    public double[] YLevels { get; }

    public SectionProfileDimensionInput(int mainPartId, double[] xLevels, double[] yLevels)
    {
        MainPartId = mainPartId;
        XLevels = (double[])(xLevels ?? throw new ArgumentNullException(nameof(xLevels))).Clone();
        YLevels = (double[])(yLevels ?? throw new ArgumentNullException(nameof(yLevels))).Clone();
    }
}
