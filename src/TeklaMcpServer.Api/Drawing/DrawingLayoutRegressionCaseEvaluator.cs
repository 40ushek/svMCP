using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutRegressionCaseEvaluator
{
    private readonly DrawingCaseSnapshotReader reader;
    private readonly DrawingLayoutStabilityAnalyzer stabilityAnalyzer;

    public DrawingLayoutRegressionCaseEvaluator()
        : this(new DrawingCaseSnapshotReader(), new DrawingLayoutStabilityAnalyzer())
    {
    }

    public DrawingLayoutRegressionCaseEvaluator(
        DrawingCaseSnapshotReader reader,
        DrawingLayoutStabilityAnalyzer stabilityAnalyzer)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.stabilityAnalyzer = stabilityAnalyzer ?? throw new ArgumentNullException(nameof(stabilityAnalyzer));
    }

    public DrawingLayoutRepeatedRunEvaluation EvaluateRepeatedRun(
        string firstRunCaseDirectory,
        string secondRunCaseDirectory,
        DrawingLayoutStabilityOptions? stabilityOptions = null)
    {
        var first = reader.Load(firstRunCaseDirectory);
        var second = reader.Load(secondRunCaseDirectory);
        return new DrawingLayoutRepeatedRunEvaluation
        {
            FirstRunCaseDirectory = first.CaseDirectory,
            SecondRunCaseDirectory = second.CaseDirectory,
            FirstRunOperation = first.Meta.Operation,
            SecondRunOperation = second.Meta.Operation,
            Stability = stabilityAnalyzer.Analyze(first.After, second.After, stabilityOptions)
        };
    }
}

internal sealed class DrawingLayoutRepeatedRunEvaluation
{
    public string FirstRunCaseDirectory { get; set; } = string.Empty;

    public string SecondRunCaseDirectory { get; set; } = string.Empty;

    public string FirstRunOperation { get; set; } = string.Empty;

    public string SecondRunOperation { get; set; } = string.Empty;

    public DrawingLayoutStabilityReport Stability { get; set; } = new();
}
