using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingBoltGeometryApi : IDrawingBoltGeometryApi
{
    private readonly Model _model;

    public TeklaDrawingBoltGeometryApi(Model model)
    {
        _model = model;
    }

    public BoltGroupGeometryInViewResult GetBoltGroupGeometryInView(int viewId, int modelId)
    {
        if (!TryGetView(viewId, out var view, out var error))
            return FailBoltGroup(viewId, modelId, error);

        return ExecuteInViewPlane(view!, () =>
        {
            var modelObject = _model.SelectModelObject(new Identifier(modelId));
            if (modelObject == null)
                return FailBoltGroup(viewId, modelId, $"Model object {modelId} not found.");

            if (modelObject is not BoltGroup boltGroup)
                return FailBoltGroup(viewId, modelId, $"Model object {modelId} is not a bolt group.");

            return new BoltGroupGeometryInViewResult
            {
                Success = true,
                ViewId = viewId,
                ModelId = modelId,
                BoltGroup = BuildBoltGroupGeometry(boltGroup)
            };
        });
    }

    public PartBoltGeometryInViewResult GetPartBoltGeometryInView(int viewId, int partId)
    {
        if (!TryGetView(viewId, out var view, out var error))
            return FailPart(viewId, partId, error);

        return ExecuteInViewPlane(view!, () =>
        {
            var modelObject = _model.SelectModelObject(new Identifier(partId));
            if (modelObject == null)
                return FailPart(viewId, partId, $"Model object {partId} not found.");

            if (modelObject is not ModelPart part)
                return FailPart(viewId, partId, $"Model object {partId} is not a part.");

            var result = new PartBoltGeometryInViewResult
            {
                Success = true,
                ViewId = viewId,
                PartId = partId
            };

            var bolts = part.GetBolts();
            if (bolts == null)
                return result;

            var seenBoltGroups = new HashSet<int>();
            while (bolts.MoveNext())
            {
                if (bolts.Current is not BoltGroup boltGroup)
                    continue;

                var boltGroupId = boltGroup.Identifier.ID;
                if (!seenBoltGroups.Add(boltGroupId))
                    continue;

                result.BoltGroups.Add(BuildBoltGroupGeometry(boltGroup));
            }

            return result;
        });
    }

    private bool TryGetView(int viewId, out View? view, out string error)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
        {
            view = null;
            error = "No drawing is currently open.";
            return false;
        }

        var viewEnumerator = activeDrawing.GetSheet().GetViews();
        while (viewEnumerator.MoveNext())
        {
            if (viewEnumerator.Current is View candidate && candidate.GetIdentifier().ID == viewId)
            {
                view = candidate;
                error = string.Empty;
                return true;
            }
        }

        view = null;
        error = $"View {viewId} not found in active drawing.";
        return false;
    }

    private T ExecuteInViewPlane<T>(View view, Func<T> action)
    {
        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(view.DisplayCoordinateSystem));

        try
        {
            return action();
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }
    }

    private static BoltGroupGeometry BuildBoltGroupGeometry(BoltGroup boltGroup)
    {
        var result = new BoltGroupGeometry
        {
            ModelId = boltGroup.Identifier.ID,
            Shape = boltGroup.GetType().Name,
            BoltType = boltGroup.BoltType.ToString(),
            BoltStandard = boltGroup.BoltStandard,
            BoltSize = boltGroup.BoltSize,
            FirstPosition = ToArray(boltGroup.FirstPosition),
            SecondPosition = ToArray(boltGroup.SecondPosition),
            PartToBeBoltedId = boltGroup.PartToBeBolted?.Identifier.ID,
            PartToBoltToId = boltGroup.PartToBoltTo?.Identifier.ID
        };

        if (boltGroup.BoltPositions != null)
        {
            var pointIndex = 0;
            foreach (var item in boltGroup.BoltPositions)
            {
                if (item is not Point point)
                    continue;

                result.Positions.Add(new BoltPointGeometry
                {
                    Index = pointIndex++,
                    Point = ToArray(point)
                });
            }
        }

        var otherParts = boltGroup.GetOtherPartsToBolt();
        if (otherParts != null)
        {
            foreach (var item in otherParts)
            {
                if (item is not ModelPart part)
                    continue;

                if (!result.OtherPartIds.Contains(part.Identifier.ID))
                    result.OtherPartIds.Add(part.Identifier.ID);
            }
        }

        TryPopulateSolidBbox(boltGroup, result);
        return result;
    }

    private static void TryPopulateSolidBbox(BoltGroup boltGroup, BoltGroupGeometry result)
    {
        try
        {
            var solid = boltGroup.GetSolid();
            if (solid == null)
                return;

            result.BboxMin = ToArray(solid.MinimumPoint);
            result.BboxMax = ToArray(solid.MaximumPoint);
        }
        catch
        {
            // Some bolt groups may not expose a solid consistently in all contexts.
            // Keep raw bolt points available even when solid bbox extraction fails.
        }
    }

    private static BoltGroupGeometryInViewResult FailBoltGroup(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };

    private static PartBoltGeometryInViewResult FailPart(int viewId, int partId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            PartId = partId,
            Error = error
        };

    private static double[] ToArray(Point? point) => point == null ? [] : [point.X, point.Y, point.Z];
}
