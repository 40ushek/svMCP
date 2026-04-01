using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal readonly struct DrawingScaleDriver
{
    public DrawingScaleDriver(double width, double height, double scale)
    {
        Width = width;
        Height = height;
        Scale = scale;
    }

    public double Width { get; }
    public double Height { get; }
    public double Scale { get; }
}

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
        IReadOnlyList<DrawingScaleDriver> scaleDrivers,
        double availableWidth,
        double availableHeight,
        double borderEstimate = 20.0)
    {
        if (scaleDrivers.Count == 0)
            throw new ArgumentException("Scale drivers must not be empty.", nameof(scaleDrivers));

        var currentScale = scaleDrivers.Select(v => v.Scale).FirstOrDefault(s => s > 0);
        if (currentScale <= 0)
            currentScale = 1.0;

        var maxModelW = 0.0;
        var maxModelH = 0.0;
        foreach (var driver in scaleDrivers)
        {
            var viewScale = driver.Scale > 0 ? driver.Scale : 1.0;

            maxModelW = Math.Max(maxModelW, driver.Width * viewScale);
            maxModelH = Math.Max(maxModelH, driver.Height * viewScale);
        }

        var minDenom = Math.Max(
            maxModelW / Math.Max(availableWidth - borderEstimate, 1),
            maxModelH / Math.Max(availableHeight - borderEstimate, 1));

        var startScale = SelectStartScale(minDenom);

        var candidates = Array.FindAll(StandardScales, s => s >= startScale);
        if (candidates.Length == 0)
            candidates = new[] { StandardScales[StandardScales.Length - 1] };

        return new DrawingScaleCandidateSelection(currentScale, minDenom, candidates);
    }

    private static double SelectStartScale(double minDenom)
    {
        if (minDenom <= StandardScales[0])
            return StandardScales[0];

        for (var i = 0; i < StandardScales.Length - 1; i++)
        {
            var lower = StandardScales[i];
            var upper = StandardScales[i + 1];
            if (minDenom > upper)
                continue;

            var midpoint = lower + ((upper - lower) / 2.0);
            return minDenom <= midpoint ? lower : upper;
        }

        return StandardScales[StandardScales.Length - 1];
    }
}
