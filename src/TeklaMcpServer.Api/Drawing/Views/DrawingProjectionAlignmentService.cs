using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed partial class DrawingProjectionAlignmentService
{
    private const double MoveEpsilon = 0.01;

    private readonly Model _model;
    private readonly TeklaDrawingPartGeometryApi _partGeometryApi;
    private readonly TeklaDrawingGridApi _gridApi;

    public DrawingProjectionAlignmentService(
        Model? model = null,
        TeklaDrawingPartGeometryApi? partGeometryApi = null,
        TeklaDrawingGridApi? gridApi = null)
    {
        _model = model ?? new Model();
        _partGeometryApi = partGeometryApi ?? new TeklaDrawingPartGeometryApi(_model);
        _gridApi = gridApi ?? new TeklaDrawingGridApi();
    }

    public ProjectionAlignmentResult Apply(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews = null,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes = null)
    {
        var result = new ProjectionAlignmentResult();

        switch (drawing)
        {
            case AssemblyDrawing assemblyDrawing:
            {
                result.Mode = "assembly";
                var front = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.FrontView);
                if (front == null)
                {
                    TraceSkip(result, "projection-skip:no-front-view");
                    return result;
                }

                ApplyAssemblyAlignment(result, assemblyDrawing, front, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;
            }

            case GADrawing:
            {
                result.Mode = "ga";
                var front = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.FrontView);
                if (front != null)
                    ApplyGaAlignment(result, front, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
                else
                    ApplyGaNeighborAlignment(result, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
                break;
            }

            default:
                TraceSkip(result, $"projection-skip:unsupported-drawing-type:{drawing.GetType().Name}");
                break;
        }

        return result;
    }
}
