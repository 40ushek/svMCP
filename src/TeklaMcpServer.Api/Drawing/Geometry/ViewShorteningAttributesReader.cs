using System;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public sealed class ViewShorteningAttributesInfo
{
    public bool CutParts { get; set; }
    public bool CutSkewParts { get; set; }
    public double MinimumLength { get; set; }
    public double Offset { get; set; }
    public double ViewScale { get; set; } = 1.0;
    public string CutPartType { get; set; } = string.Empty;
    public double SpaceBetweenCutParts => CutParts ? Math.Max(0.0, Offset) : 0.0;
    public double SpaceBetweenCutPartsInViewCoordinates => SpaceBetweenCutParts * Math.Max(1.0, ViewScale);
}

public static class ViewShorteningAttributesReader
{
    public static ViewShorteningAttributesInfo Read(View view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        var shortening = view.Attributes?.Shortening;
        var viewScale = view.Attributes?.Scale > 0 ? view.Attributes.Scale : 1.0;
        if (shortening == null)
            return new ViewShorteningAttributesInfo { ViewScale = viewScale };

        return new ViewShorteningAttributesInfo
        {
            CutParts = shortening.CutParts,
            CutSkewParts = shortening.CutSkewParts,
            MinimumLength = shortening.MinimumLength,
            Offset = shortening.Offset,
            ViewScale = viewScale,
            CutPartType = shortening.CutPartType.ToString()
        };
    }

    public static double GetSpaceBetweenCutPartsInViewCoordinates(View view)
    {
        return Read(view).SpaceBetweenCutPartsInViewCoordinates;
    }
}
