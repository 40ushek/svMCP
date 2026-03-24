using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed partial class DrawingProjectionAlignmentService
{
    private const double MoveEpsilon = 0.01;
    private const double ProjectionViewGap = 5.0; // mm gap when repositioning to clear a conflicting view

    private readonly Model _model;
    private readonly TeklaDrawingPartGeometryApi _partGeometryApi;
    private readonly TeklaDrawingGridApi _gridApi;
    private readonly SectionPlacementSideResolver _sectionPlacementSideResolver;

    public DrawingProjectionAlignmentService(
        Model? model = null,
        TeklaDrawingPartGeometryApi? partGeometryApi = null,
        TeklaDrawingGridApi? gridApi = null)
    {
        _model = model ?? new Model();
        _partGeometryApi = partGeometryApi ?? new TeklaDrawingPartGeometryApi(_model);
        _gridApi = gridApi ?? new TeklaDrawingGridApi();
        _sectionPlacementSideResolver = new SectionPlacementSideResolver(_model);
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
        var semanticViews = SemanticViewSet.Build(views);
        var baseSelection = BaseViewSelection.Select(views);
        var neighbors = baseSelection.View != null
            ? StandardNeighborResolver.Build(views, semanticViews, baseSelection)
            : null;

        switch (drawing)
        {
            case AssemblyDrawing assemblyDrawing:
            {
                result.Mode = "assembly";
                if (neighbors == null)
                {
                    TraceSkip(result, "projection-skip:no-base-view");
                    return result;
                }

                ApplyAssemblyAlignment(result, assemblyDrawing, neighbors, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;
            }

            case SinglePartDrawing singlePartDrawing:
            {
                result.Mode = "single-part";
                if (neighbors == null)
                {
                    TraceSkip(result, "projection-skip:no-base-view");
                    return result;
                }

                ApplySinglePartAlignment(result, singlePartDrawing, neighbors, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;
            }

            case GADrawing gaDrawing:
            {
                result.Mode = "ga";
                if (neighbors != null)
                    ApplyGaAlignment(result, gaDrawing, neighbors, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
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

    private bool TryGetSectionAlignmentAxis(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingView baseView,
        DrawingView sectionView,
        ProjectionAlignmentResult result,
        out bool alignX)
    {
        var sectionSide = _sectionPlacementSideResolver.Resolve(drawing, baseView, sectionView);
        if (DrawingProjectionAlignmentMath.TryGetSectionAlignmentAxis(sectionSide.PlacementSide, out alignX))
            return true;

        TraceSkip(
            result,
            $"projection-skip:section-side-unknown:reason={sectionSide.Reason}");
        return false;
    }
}
