using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingPartSolidGeometryApi : IDrawingPartSolidGeometryApi
{
    private readonly Model _model;

    public TeklaDrawingPartSolidGeometryApi(Model model)
    {
        _model = model;
    }

    public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId)
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
            return Fail(viewId, modelId, "No drawing is currently open.");

        View? view = null;
        var viewEnumerator = activeDrawing.GetSheet().GetViews();
        while (viewEnumerator.MoveNext())
        {
            if (viewEnumerator.Current is View candidate && candidate.GetIdentifier().ID == viewId)
            {
                view = candidate;
                break;
            }
        }

        if (view == null)
            return Fail(viewId, modelId, $"View {viewId} not found in active drawing.");

        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        // Solid geometry is consumed as 2D geometry in the drawing view, so read it in the view CS.
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(view.ViewCoordinateSystem));

        try
        {
            var modelObject = _model.SelectModelObject(new Identifier(modelId));
            if (modelObject == null)
                return Fail(viewId, modelId, $"Model object {modelId} not found.");

            if (modelObject is not ModelPart part)
                return Fail(viewId, modelId, $"Model object {modelId} is not a part.");

            var solid = part.GetSolid();
            if (solid == null)
                return Fail(viewId, modelId, $"Model object {modelId} does not expose solid geometry.");

            return new PartSolidGeometryInViewResult
            {
                Success = true,
                ViewId = viewId,
                ModelId = modelId,
                StartPoint = part is Beam beam ? ToArray(beam.StartPoint) : [],
                EndPoint = part is Beam endPointBeam ? ToArray(endPointBeam.EndPoint) : [],
                Solid = BuildSolidGeometry(solid)
            };
        }
        catch (Exception ex)
        {
            return Fail(viewId, modelId, ex.Message);
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }
    }

    private static PartSolidGeometry BuildSolidGeometry(Solid solid)
    {
        // One canonical traversal supplies vertices, loop indexes and the hull.
        var snapshot = SolidViewVertexCollector.CollectGeometry(
            solid,
            tolerateTraversalErrors: false);

        var result = new PartSolidGeometry
        {
            BboxMin = ToArray(solid.MinimumPoint),
            BboxMax = ToArray(solid.MaximumPoint),
            SolidGeometryComplete = true,
            Vertices = snapshot.Vertices
                .Select(static (point, index) => new PartVertexGeometry
                {
                    Index = index,
                    Point = [point[0], point[1], point[2]]
                })
                .ToList()
        };

        for (var faceIndex = 0; faceIndex < snapshot.Faces.Count; faceIndex++)
        {
            var face = snapshot.Faces[faceIndex];
            var faceGeometry = new PartFaceGeometry
            {
                Index = faceIndex,
                Normal = face.Normal is null ? null : face.Normal.ToArray()
            };

            for (var loopIndex = 0; loopIndex < face.Loops.Count; loopIndex++)
            {
                var loopGeometry = new PartLoopGeometry
                {
                    Index = loopIndex
                };
                loopGeometry.VertexIndexes.AddRange(face.Loops[loopIndex]);
                faceGeometry.Loops.Add(loopGeometry);
            }

            result.Faces.Add(faceGeometry);
        }

        result.ViewHull = PartViewGeometryBuilder.BuildHull(snapshot.Vertices);

        return result;
    }

    private static PartSolidGeometryInViewResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };

    private static double[] ToArray(Point? point) => point == null ? [] : [point.X, point.Y, point.Z];
}
