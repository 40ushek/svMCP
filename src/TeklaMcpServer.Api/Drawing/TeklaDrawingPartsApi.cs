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

    private static DrawingPartInfo? BuildInfo(Tekla.Structures.Model.ModelObject mo)
    {
        var info = new DrawingPartInfo
        {
            ModelId = mo.Identifier.ID,
            Type    = mo.GetType().Name
        };

        string s = string.Empty;

        mo.GetReportProperty("PART_POS",     ref s); info.PartPos     = s; s = string.Empty;
        mo.GetReportProperty("ASSEMBLY_POS", ref s); info.AssemblyPos = s; s = string.Empty;
        mo.GetReportProperty("PROFILE",      ref s); info.Profile     = s; s = string.Empty;
        mo.GetReportProperty("MATERIAL",     ref s); info.Material    = s; s = string.Empty;
        mo.GetReportProperty("NAME",         ref s); info.Name        = s;

        return info;
    }
}
