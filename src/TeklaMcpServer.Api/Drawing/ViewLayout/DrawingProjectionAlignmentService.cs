using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;
using DrawingView = Tekla.Structures.Drawing.View;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

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
        return ApplyCore(
            drawing,
            views,
            frameOffsetsById,
            sheetWidth,
            sheetHeight,
            margin,
            reservedAreas,
            ViewTopologyGraph.Build(views),
            arrangedViews,
            preloadedAxes);
    }

    public ProjectionAlignmentResult Apply(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<DrawingView> views,
        IList<ArrangedView>? arrangedViews = null)
    {
        if (workspace == null)
            throw new System.ArgumentNullException(nameof(workspace));

        return ApplyCore(
            drawing,
            views,
            workspace.FrameOffsetsById,
            workspace.SheetWidth,
            workspace.SheetHeight,
            workspace.Margin,
            workspace.ReservedAreas,
            workspace.GetTopology(views),
            arrangedViews,
            workspace.GridAxesByViewId);
    }

    private ProjectionAlignmentResult ApplyCore(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        ViewTopologyGraph topology,
        IList<ArrangedView>? arrangedViews,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes)
    {
        var result = new ProjectionAlignmentResult();
        var neighbors = topology.Neighbors;

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

                ApplyAssemblyAlignment(result, assemblyDrawing, topology, neighbors, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
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

                ApplySinglePartAlignment(result, singlePartDrawing, topology, neighbors, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;
            }

            case GADrawing gaDrawing:
            {
                result.Mode = "ga";
                if (neighbors != null)
                    ApplyGaAlignment(result, gaDrawing, topology, neighbors, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
                else
                    ApplyGaNeighborAlignment(result, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
                break;
            }

            default:
                TraceSkip(result, $"projection-skip:unsupported-drawing-type:{drawing.GetType().Name}");
                break;
        }

        TraceProjectionParitySummary(result);
        return result;
    }

    private static void TraceProjectionParitySummary(ProjectionAlignmentResult result)
    {
        PerfTrace.Write(
            "api-view",
            "projection_parity_summary",
            0,
            $"mode={result.Mode} applied={result.AppliedMoves} skipped={result.SkippedMoves} outOfBoundsRejects={result.OutOfBoundsRejects} reservedOverlapRejects={result.ReservedOverlapRejects} viewOverlapRejects={result.ViewOverlapRejects} diagnostics={result.Diagnostics.Count}");
    }

    internal static bool TryResolveSectionAlignmentAxis(
        ArrangedView? arrangedView,
        SectionPlacementSide resolvedPlacementSide,
        out bool alignX,
        out string reason)
    {
        if (DrawingProjectionAlignmentMath.TryGetSectionAlignmentAxis(resolvedPlacementSide, out alignX))
        {
            reason = string.Empty;
            return true;
        }

        if (arrangedView != null)
        {
            if (string.IsNullOrWhiteSpace(arrangedView.ActualPlacementSide))
            {
                alignX = false;
                reason = $"projection-skip:section-unresolved:view={arrangedView.Id}";
                return false;
            }

            if (System.Enum.TryParse<SectionPlacementSide>(arrangedView.ActualPlacementSide, ignoreCase: true, out var actualPlacementSide)
                && DrawingProjectionAlignmentMath.TryGetSectionAlignmentAxis(actualPlacementSide, out alignX))
            {
                reason = string.Empty;
                return true;
            }
        }

        alignX = false;
        reason = $"projection-skip:section-side-unknown:reason={resolvedPlacementSide}";
        return false;
    }

    private bool TryGetSectionAlignmentAxis(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingView baseView,
        int sectionId,
        DrawingView sectionView,
        ProjectionAlignmentResult result,
        IList<ArrangedView>? arrangedViews,
        out bool alignX)
    {
        var sectionSide = _sectionPlacementSideResolver.Resolve(drawing, baseView, sectionView);
        var arrangedView = arrangedViews?.FirstOrDefault(item => item.Id == sectionId);
        if (TryResolveSectionAlignmentAxis(arrangedView, sectionSide.PlacementSide, out alignX, out var reason))
            return true;

        TraceSkip(
            result,
            reason == $"projection-skip:section-side-unknown:reason={sectionSide.PlacementSide}"
                ? $"projection-skip:section-side-unknown:reason={sectionSide.Reason}"
                : reason);
        return false;
    }
}

