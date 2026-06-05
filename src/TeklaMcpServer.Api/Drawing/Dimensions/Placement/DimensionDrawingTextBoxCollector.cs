using System.Collections.Generic;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionDrawingTextBoxCollector
{
    internal static List<DrawingTextBox> Collect(
        PresentationConnection? connection,
        int sourceObjectId,
        string sourceObjectKind,
        DrawingView view,
        ViewShorteningCoordinateMapper? shorteningMapper = null,
        DimensionTextBoxShorteningMode shorteningMode = DimensionTextBoxShorteningMode.None)
    {
        var presentationBoxes = DimensionPresentationTextBoxCollector.Collect(
            connection,
            sourceObjectId,
            sourceObjectKind,
            view);

        return DimensionDrawingTextBoxMapper.ToDrawingTextBoxes(presentationBoxes, shorteningMapper, shorteningMode);
    }

    internal static List<DrawingTextBox> CollectDistinct(
        PresentationConnection? connection,
        IEnumerable<DimensionDrawingTextBoxSource> sources,
        DrawingView view,
        ViewShorteningCoordinateMapper? shorteningMapper = null,
        DimensionTextBoxShorteningMode shorteningMode = DimensionTextBoxShorteningMode.None)
    {
        var presentationBoxes = new List<DimensionPresentationTextBox>();

        foreach (var source in sources)
        {
            presentationBoxes.AddRange(DimensionPresentationTextBoxCollector.Collect(
                connection,
                source.SourceObjectId,
                source.SourceObjectKind,
                view));
        }

        var distinctPresentationBoxes = DimensionPresentationTextBoxCollector.DistinctByGeometry(presentationBoxes);
        return DimensionDrawingTextBoxMapper.ToDrawingTextBoxes(distinctPresentationBoxes, shorteningMapper, shorteningMode);
    }
}

internal readonly struct DimensionDrawingTextBoxSource
{
    public DimensionDrawingTextBoxSource(int sourceObjectId, string sourceObjectKind)
    {
        SourceObjectId = sourceObjectId;
        SourceObjectKind = sourceObjectKind;
    }

    public int SourceObjectId { get; }

    public string SourceObjectKind { get; }
}
