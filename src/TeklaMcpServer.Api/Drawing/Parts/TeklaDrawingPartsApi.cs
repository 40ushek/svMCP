using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingPartsApi : IDrawingPartsApi
{
    private readonly Model _model;

    public TeklaDrawingPartsApi(Model model) => _model = model;

    public GetDrawingPartsResult GetDrawingParts()
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing  = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        // Direct lookup — no sheet enumeration needed
        var identifiers = drawingHandler.GetModelObjectIdentifiers(activeDrawing);

        var parts = new List<DrawingPartInfo>();

        foreach (Tekla.Structures.Identifier id in identifiers)
        {
            var mo = _model.SelectModelObject(id);
            if (mo == null) continue;

            var info = BuildInfo(mo);
            if (info != null)
                parts.Add(info);
        }

        return new GetDrawingPartsResult { Total = parts.Count, Parts = parts };
    }

    /// <summary>
    /// The select is not optional and not defensive. Report properties come back empty
    /// unless the object is fetched from the model first, and a whole drawing of blank
    /// prefixes reads exactly like a model whose parts have none - which is then written
    /// into an exclusion filter that matches nothing.
    ///
    /// The assembly is the shape of the answer, and it lives in DrawingPartInfoBuilder so
    /// that both the select and the difference between "empty" and "unreadable" can be
    /// held by a test without Tekla running.
    /// </summary>
    private static DrawingPartInfo? BuildInfo(Tekla.Structures.Model.ModelObject mo) =>
        DrawingPartInfoBuilder.Build(
            mo.Identifier.ID,
            mo.GetType().Name,
            () => mo.Select(),
            property =>
            {
                var value = string.Empty;
                var read = mo.GetReportProperty(property, ref value);
                return new DrawingPartInfoBuilder.PropertyRead(read, value);
            });
}
