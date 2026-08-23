using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Shared boundary for geometry that must describe what one drawing view shows.</summary>
internal static class DrawingViewParts
{
    public static View? FindView(Tekla.Structures.Drawing.Drawing drawing, int viewId)
    {
        var views = drawing.GetSheet().GetViews();
        while (views.MoveNext())
        {
            if (views.Current is View candidate && candidate.GetIdentifier().ID == viewId)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Model ids referenced by non-hidden drawing objects, each once. This is deliberately
    /// only a cheap candidate list: for a section/end view it is not evidence that Tekla
    /// renders a part inside that view's depth window.
    /// </summary>
    public static IEnumerable<int> CandidateModelIds(View view)
    {
        var seen = new HashSet<int>();
        var objects = view.GetObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not DrawingPart drawingPart || IsHidden(drawingPart))
                continue;

            var id = drawingPart.ModelIdentifier.ID;
            if (seen.Add(id))
                yield return id;
        }
    }

    /// <summary>
    /// Resolves candidate parts against a section/end view's depth window. This is an
    /// intentional broad phase: each solid's view-aligned bounding box is compared with
    /// the view's RestrictionBox. It prevents a mid-span section from losing a long beam,
    /// but can include a skewed or complex solid whose box overlaps while the solid does not.
    /// </summary>
    public static DepthFilteredParts GetDepthFilteredParts(
        Model model,
        View view,
        ViewDepthWindow? depthWindow = null)
    {
        var candidates = CandidateModelIds(view).ToList();
        if (!LooksAlongMemberLength(view.ViewType))
            return new DepthFilteredParts(candidates, candidates, [], [], []);

        var unread = new List<UnreadPart>();
        var included = new List<int>();
        var outsideDepth = new List<int>();
        var boundaryAmbiguous = new List<UnreadPart>();
        var viewCoordinateSystem = view.ViewCoordinateSystem;
        if (DrawingViewPlane.IsModelPlane(viewCoordinateSystem))
        {
            unread.AddRange(candidates.Select(id => new UnreadPart(id, DrawingViewPlane.ModelPlaneReason)));
            return new DepthFilteredParts(candidates, included, outsideDepth, boundaryAmbiguous, unread);
        }

        depthWindow ??= ViewDepthWindow.Read(model, view);
        if (!depthWindow.IsUsable)
        {
            unread.AddRange(candidates.Select(id => new UnreadPart(id, depthWindow.Error ?? "RestrictionBox could not be read")));
            return new DepthFilteredParts(candidates, included, outsideDepth, boundaryAmbiguous, unread);
        }
        var restrictionBox = depthWindow.Box!.Value;

        var workPlaneHandler = model.GetWorkPlaneHandler();
        TransformationPlane originalPlane;
        try
        {
            originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
            workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(viewCoordinateSystem));
        }
        catch (System.Exception exception)
        {
            unread.AddRange(candidates.Select(id => new UnreadPart(id, $"View coordinate system could not be selected: {exception.Message}")));
            return new DepthFilteredParts(candidates, included, outsideDepth, boundaryAmbiguous, unread);
        }

        try
        {
            foreach (var modelId in candidates)
            {
                try
                {
                    if (model.SelectModelObject(new Identifier(modelId)) is not ModelPart part)
                    {
                        unread.Add(new UnreadPart(modelId, "the view names it but the model does not have it as a part"));
                        continue;
                    }

                    var solid = part.GetSolid();
                    if (solid == null)
                    {
                        unread.Add(new UnreadPart(modelId, "the model part does not expose solid geometry"));
                        continue;
                    }

                    var solidBox = new DepthBox(
                        solid.MinimumPoint.X, solid.MinimumPoint.Y, solid.MinimumPoint.Z,
                        solid.MaximumPoint.X, solid.MaximumPoint.Y, solid.MaximumPoint.Z);
                    switch (restrictionBox.Classify(solidBox))
                    {
                        case DepthBoxRelation.Disjoint:
                            outsideDepth.Add(modelId);
                            continue;

                        case DepthBoxRelation.Invalid:
                            unread.Add(new UnreadPart(modelId, "solid bounding box is degenerate or contains non-finite coordinates"));
                            continue;

                        case DepthBoxRelation.BoundaryTouch:
                            boundaryAmbiguous.Add(new UnreadPart(
                                modelId,
                                "depth=BoundaryAmbiguous: solid bounding box touches RestrictionBox boundary"));
                            continue;

                        case DepthBoxRelation.Overlaps:
                            included.Add(modelId);
                            continue;
                    }
                }
                catch (System.Exception exception)
                {
                    unread.Add(new UnreadPart(modelId, exception.Message));
                }
            }
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }

        return new DepthFilteredParts(candidates, included, outsideDepth, boundaryAmbiguous, unread);
    }

    internal static bool LooksAlongMemberLength(View.ViewTypes viewType) =>
        viewType is View.ViewTypes.SectionView or View.ViewTypes.EndView;

    private static bool IsHidden(DrawingPart drawingPart)
    {
        try
        {
            return drawingPart.Hideable.IsHidden;
        }
        catch
        {
            // A part that will not report its visibility is kept. Omitting it would turn
            // uncertainty into a false boundary; the caller can still report a read error.
            return false;
        }
    }
}

internal sealed class DepthFilteredParts
{
    public DepthFilteredParts(
        IReadOnlyList<int> candidateModelIds,
        IReadOnlyList<int> modelIds,
        IReadOnlyList<int> outsideDepthModelIds,
        IReadOnlyList<UnreadPart> boundaryAmbiguous,
        IReadOnlyList<UnreadPart> unread)
    {
        CandidateModelIds = candidateModelIds;
        ModelIds = modelIds;
        OutsideDepthModelIds = outsideDepthModelIds;
        BoundaryAmbiguous = boundaryAmbiguous;
        Unread = unread;
    }

    public IReadOnlyList<int> CandidateModelIds { get; }
    public IReadOnlyList<int> ModelIds { get; }
    public IReadOnlyList<int> OutsideDepthModelIds { get; }
    public IReadOnlyList<UnreadPart> BoundaryAmbiguous { get; }
    public IReadOnlyList<UnreadPart> Unread { get; }
    public IEnumerable<UnreadPart> Incomplete => BoundaryAmbiguous.Concat(Unread);
}
