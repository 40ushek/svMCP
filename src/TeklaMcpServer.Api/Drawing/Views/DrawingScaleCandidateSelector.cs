using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal readonly struct DrawingScaleCandidateSelection
{
    public DrawingScaleCandidateSelection(double currentScale, double minDenom, IReadOnlyList<double> candidates)
    {
        CurrentScale = currentScale;
        MinDenom = minDenom;
        Candidates = candidates;
    }

    public double CurrentScale { get; }
    public double MinDenom { get; }
    public IReadOnlyList<double> Candidates { get; }
}

internal static class DrawingScaleCandidateSelector
{
    private static readonly double[] StandardScales = { 1.0, 2, 5, 10, 15, 20, 25, 30, 40, 50, 60, 70, 75, 80, 100, 125, 150, 175, 200, 250, 300 };

    public static DrawingScaleCandidateSelection Select(
        IReadOnlyList<View> scaleDriverViews,
        double availableWidth,
        double availableHeight,
        double borderEstimate = 20.0)
    {
        if (scaleDriverViews.Count == 0)
            throw new ArgumentException("Scale driver views must not be empty.", nameof(scaleDriverViews));

        var currentScale = scaleDriverViews.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);
        if (currentScale <= 0)
            currentScale = 1.0;

        var maxModelW = scaleDriverViews.Max(v => v.Width * currentScale);
        var maxModelH = scaleDriverViews.Max(v => v.Height * currentScale);
        var minDenom = Math.Max(
            maxModelW / Math.Max(availableWidth - borderEstimate, 1),
            maxModelH / Math.Max(availableHeight - borderEstimate, 1));

        var candidates = Array.FindAll(StandardScales, s => s >= minDenom);
        if (candidates.Length == 0)
            candidates = new[] { StandardScales[StandardScales.Length - 1] };

        return new DrawingScaleCandidateSelection(currentScale, minDenom, candidates);
    }
}
