using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
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
            return;

        var sources = CollectDimensionTextBoxSources(view);
        context.DimensionTextBoxes.AddRange(DimensionDrawingTextBoxCollector.CollectDistinct(
            presentationConnection,
            sources,
            view));
    }

    private static List<DimensionDrawingTextBoxSource> CollectDimensionTextBoxSources(View view)
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
}
