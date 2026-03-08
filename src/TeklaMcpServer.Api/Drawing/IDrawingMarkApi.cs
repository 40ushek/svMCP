namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingMarkApi
{
    /// <summary>
    /// Returns all marks on the active drawing, optionally filtered to a single view.
    /// Each mark includes its property elements (name + computed value).
    /// </summary>
    GetMarksResult    GetMarks(int? viewId);
    ResolveMarksResult ResolveMarkOverlaps(double margin);
    ResolveMarksResult ArrangeMarks(double gap);

    CreateMarksResult CreatePartMarks(string contentAttributesCsv, string markAttributesFile, string frameType, string arrowheadType);
    SetMarkContentResult SetMarkContent(SetMarkContentRequest request);
}
