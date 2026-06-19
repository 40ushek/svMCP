using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Diagnostics;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionTextBoxContextLoader
{
    internal static PresentationConnection? TryCreatePresentationConnection()
    {
        try
        {
            return new PresentationConnection();
        }
        catch
        {
            return null;
        }
    }

    internal static void PopulateDimensionTextBoxes(
        DrawingViewContext context,
        View view,
        PresentationConnection? presentationConnection)
    {
        context.DimensionTextBoxes.Clear();
        if (presentationConnection == null)
        {
            WriteTrace(view, "presentation=null sources=0 boxes=0 shorteningMode=none hasShortening=false");
            return;
        }

        // Mapper is built and consumed here, never exposed to other layers.
        // Direction is controlled by SVMCP_DIM_SHORTENING_MODE env var:
        // none (default), toVisual, toRaw. Until empirical smoke confirms which
        // direction matches the production coordinate system, no conversion is
        // applied by default — keeping current production behavior unchanged.
        var shorteningMapper = TryBuildShorteningMapper(view);
        var requestedMode = DimensionTextBoxShorteningModeResolver.Resolve();
        var effectiveMode =
            shorteningMapper != null && shorteningMapper.HasShortening
                ? requestedMode
                : DimensionTextBoxShorteningMode.None;
        context.AppliedDimensionTextBoxShorteningMode = effectiveMode.ToString().ToLowerInvariant();

        var sources = CollectDimensionTextBoxSources(view);
        var textBoxes = DimensionDrawingTextBoxCollector.CollectDistinct(
            presentationConnection,
            sources,
            view,
            shorteningMapper,
            effectiveMode);
        if (textBoxes.Count == 0)
            textBoxes.AddRange(CollectRuntimeFallbackTextBoxes(view, shorteningMapper, effectiveMode));

        context.DimensionTextBoxes.AddRange(textBoxes);
        WriteTrace(
            view,
            $"presentation=connected sources={sources.Count} boxes={textBoxes.Count} " +
            $"shorteningMode={context.AppliedDimensionTextBoxShorteningMode} " +
            $"hasShortening={shorteningMapper?.HasShortening == true}");
    }

    private static ViewShorteningCoordinateMapper? TryBuildShorteningMapper(View view)
    {
        try
        {
            var boxes = ReadVisibleAreaRestrictionBoxes(view);
            if (boxes.Count == 0)
                return null;

            var shortening = ViewShorteningAttributesReader.Read(view);
            return ViewShorteningCoordinateMapper.FromAabbs(
                boxes,
                shortening.SpaceBetweenCutPartsInViewCoordinates);
        }
        catch
        {
            return null;
        }
    }

    private static List<AABB> ReadVisibleAreaRestrictionBoxes(View view)
    {
        var result = new List<AABB>();
        var boxes = view.GetVisibleAreaRestrictionBoxes();
        while (boxes.MoveNext())
        {
            if (boxes.Current is AABB box)
                result.Add(box);
        }

        return result;
    }

    internal static List<DimensionDrawingTextBoxSource> CollectDimensionTextBoxSources(View view)
    {
        var sources = new List<DimensionDrawingTextBoxSource>();

        CollectStraightDimensionSources(view, sources);
        CollectObjectSourcesWithAutoFetch<AngleDimension>(view, "angleDimension", sources);
        CollectObjectSourcesWithAutoFetch<RadiusDimension>(view, "radiusDimension", sources);

        return sources;
    }

    private static void CollectStraightDimensionSources(
        View view,
        List<DimensionDrawingTextBoxSource> sources)
    {
        DrawingObjectEnumerator objects;
        try
        {
            objects = view.GetAllObjects(typeof(StraightDimensionSet));
        }
        catch
        {
            return;
        }

        while (objects.MoveNext())
        {
            StraightDimensionSet dimSet;
            try
            {
                if (objects.Current is not StraightDimensionSet current)
                    continue;

                dimSet = current;
                // Query both the set and its segments: Tekla presentation may expose
                // straight dimension text primitives at either level depending on shape.
                sources.Add(new DimensionDrawingTextBoxSource(dimSet.GetIdentifier().ID, "dimensionSet"));
            }
            catch
            {
                continue;
            }

            DrawingObjectEnumerator segments;
            try
            {
                segments = dimSet.GetObjects();
            }
            catch
            {
                continue;
            }

            while (segments.MoveNext())
            {
                try
                {
                    if (segments.Current is StraightDimension segment)
                        sources.Add(new DimensionDrawingTextBoxSource(segment.GetIdentifier().ID, "segment"));
                }
                catch
                {
                    // Keep collecting remaining segment sources when one segment is unstable.
                }
            }
        }
    }

    private static void CollectObjectSourcesWithAutoFetch<TDimension>(
        View view,
        string sourceObjectKind,
        List<DimensionDrawingTextBoxSource> sources)
        where TDimension : DrawingObject
    {
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = true;
        try
        {
            var objects = view.GetAllObjects(typeof(TDimension));
            while (objects.MoveNext())
            {
                if (objects.Current is TDimension dimension)
                    sources.Add(new DimensionDrawingTextBoxSource(
                        dimension.GetIdentifier().ID,
                        sourceObjectKind));
            }
        }
        catch
        {
            // Dimension text blockers are best-effort: mark layout can continue without them.
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private static List<DrawingTextBox> CollectRuntimeFallbackTextBoxes(
        View view,
        ViewShorteningCoordinateMapper? shorteningMapper,
        DimensionTextBoxShorteningMode shorteningMode)
    {
        var presentationBoxes = new List<DimensionPresentationTextBox>();

        DrawingObjectEnumerator objects;
        try
        {
            objects = view.GetAllObjects(typeof(StraightDimensionSet));
        }
        catch
        {
            return [];
        }

        while (objects.MoveNext())
        {
            if (objects.Current is not StraightDimensionSet dimSet)
                continue;

            var addedForSet = 0;
            var segments = EnumerateSegments(dimSet);
            foreach (var segment in segments)
            {
                var candidates = DimensionTextBoxCollector.Collect(segment, dimSet, FrameTypes.None);
                foreach (var candidate in candidates)
                {
                    if (candidate.Polygon.Count < 4)
                        continue;

                    presentationBoxes.Add(CreatePresentationFallbackBox(
                        segment.GetIdentifier().ID,
                        candidate.Owner,
                        candidate.Text,
                        candidate.Polygon));
                    addedForSet++;
                }
            }

            if (addedForSet > 0)
                continue;

            foreach (var candidate in TeklaDrawingDimensionsApi.CollectFallbackTextPolygons(dimSet))
            {
                if (candidate.Polygon.Count < 4)
                    continue;

                presentationBoxes.Add(CreatePresentationFallbackBox(
                    dimSet.GetIdentifier().ID,
                    candidate.Owner,
                    candidate.Text,
                    candidate.Polygon));
            }
        }

        var distinctPresentationBoxes = DimensionPresentationTextBoxCollector.DistinctByGeometry(presentationBoxes);
        return DimensionDrawingTextBoxMapper.ToDrawingTextBoxes(
            distinctPresentationBoxes,
            shorteningMapper,
            shorteningMode);
    }

    private static List<StraightDimension> EnumerateSegments(StraightDimensionSet dimSet)
    {
        var result = new List<StraightDimension>();
        DrawingObjectEnumerator segments;
        try
        {
            segments = dimSet.GetObjects();
        }
        catch
        {
            return result;
        }

        while (segments.MoveNext())
        {
            if (segments.Current is StraightDimension segment)
                result.Add(segment);
        }

        return result;
    }

    private static DimensionPresentationTextBox CreatePresentationFallbackBox(
        int sourceObjectId,
        string sourceObjectKind,
        string text,
        List<double[]> polygon)
    {
        GetBounds(polygon, out var minX, out var minY, out var maxX, out var maxY);
        return new DimensionPresentationTextBox
        {
            SourceObjectId = sourceObjectId,
            SourceObjectKind = sourceObjectKind,
            Text = text,
            CenterX = (minX + maxX) / 2.0,
            CenterY = (minY + maxY) / 2.0,
            ViewPositionX = minX,
            ViewPositionY = minY,
            ViewWidth = maxX - minX,
            ViewHeight = maxY - minY,
            Polygon = polygon
        };
    }

    private static void GetBounds(
        IReadOnlyList<double[]> polygon,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = double.PositiveInfinity;
        minY = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        maxY = double.NegativeInfinity;

        foreach (var point in polygon)
        {
            if (point.Length < 2)
                continue;

            minX = System.Math.Min(minX, point[0]);
            minY = System.Math.Min(minY, point[1]);
            maxX = System.Math.Max(maxX, point[0]);
            maxY = System.Math.Max(maxY, point[1]);
        }

        if (double.IsInfinity(minX))
        {
            minX = minY = maxX = maxY = 0.0;
        }
    }

    private static void WriteTrace(View view, string details)
    {
        if (!PerfTrace.IsDetailedTraceActive)
            return;

        PerfTrace.Write(
            "api-mark",
            "dimension_text_box_loader",
            0,
            $"viewId={ResolveViewId(view)} {details}");
    }

    private static string ResolveViewId(View view)
    {
        try { return view.GetIdentifier().ID.ToString(); }
        catch { return "<unknown>"; }
    }
}
