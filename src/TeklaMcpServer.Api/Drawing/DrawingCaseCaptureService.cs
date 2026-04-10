using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingCaseCaptureService
{
    private readonly Func<GetDrawingLayoutContextResult> _captureLayoutContext;
    private readonly DrawingLayoutScorer _scorer;
    private readonly DrawingCaseSnapshotWriter _writer;

    public DrawingCaseCaptureService()
        : this(
            () => new TeklaDrawingContextApi().GetLayoutContext(),
            new DrawingLayoutScorer(),
            new DrawingCaseSnapshotWriter())
    {
    }

    internal DrawingCaseCaptureService(
        Func<GetDrawingLayoutContextResult> captureLayoutContext,
        DrawingLayoutScorer scorer,
        DrawingCaseSnapshotWriter writer)
    {
        _captureLayoutContext = captureLayoutContext ?? throw new ArgumentNullException(nameof(captureLayoutContext));
        _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public DrawingContext CaptureDrawingContext()
    {
        var result = _captureLayoutContext();
        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Failed to capture drawing layout context." : result.Error);

        return result.Context ?? throw new InvalidOperationException("Drawing layout context capture returned no context.");
    }

    public DrawingCaseSnapshotSaveResult SaveCase(
        DrawingContext before,
        DrawingContext after,
        string rootDirectory,
        string drawingCategory,
        string operation,
        string? note = null)
    {
        if (before == null)
            throw new ArgumentNullException(nameof(before));

        if (after == null)
            throw new ArgumentNullException(nameof(after));

        ValidateSameDrawingGuid(before, after);

        var scoreBefore = _scorer.Score(before);
        var scoreAfter = _scorer.Score(after);

        return _writer.Save(
            rootDirectory,
            drawingCategory,
            operation,
            before,
            after,
            note,
            scoreBefore,
            scoreAfter);
    }

    private static void ValidateSameDrawingGuid(DrawingContext before, DrawingContext after)
    {
        var beforeGuid = before.Drawing?.Guid?.Trim();
        var afterGuid = after.Drawing?.Guid?.Trim();

        if (string.IsNullOrWhiteSpace(beforeGuid) || string.IsNullOrWhiteSpace(afterGuid))
            throw new InvalidOperationException("SaveCase requires drawing_guid in both before and after contexts.");

        if (!string.Equals(beforeGuid, afterGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SaveCase requires before and after contexts to reference the same drawing_guid.");
    }
}
