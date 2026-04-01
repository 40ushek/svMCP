using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;
using SolidTypes = Tekla.Structures.Solid;

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
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(view.DisplayCoordinateSystem));

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
        var result = new PartSolidGeometry
        {
            BboxMin = ToArray(solid.MinimumPoint),
            BboxMax = ToArray(solid.MaximumPoint)
        };

        var faceEnumerator = solid.GetFaceEnumerator();
        var faceIndex = 0;
        while (faceEnumerator.MoveNext())
        {
            if (faceEnumerator.Current is not SolidTypes.Face face)
                continue;

            var faceGeometry = new PartFaceGeometry
            {
                Index = faceIndex++,
                Normal = ToArray(face.Normal)
            };

            var loopEnumerator = face.GetLoopEnumerator();
            var loopIndex = 0;
            while (loopEnumerator.MoveNext())
            {
                if (loopEnumerator.Current is not SolidTypes.Loop loop)
                    continue;

                var loopGeometry = new PartLoopGeometry
                {
                    Index = loopIndex++
                };

                if (loop.GetVertexEnumerator() is SolidTypes.VertexEnumerator vertexEnumerator)
                {
                    while (vertexEnumerator.MoveNext())
                    {
                        if (vertexEnumerator.Current is not Point vertex)
                            continue;

                        var vertexIndex = GetOrAddVertex(result.Vertices, vertex);
                        loopGeometry.VertexIndexes.Add(vertexIndex);
                    }
                }

                faceGeometry.Loops.Add(loopGeometry);
            }

            result.Faces.Add(faceGeometry);
        }

        return result;
    }

    private static int GetOrAddVertex(List<PartVertexGeometry> vertices, Point point)
    {
        for (var i = 0; i < vertices.Count; i++)
        {
            if (SamePoint(vertices[i].Point, point))
                return vertices[i].Index;
        }

        var index = vertices.Count;
        vertices.Add(new PartVertexGeometry
        {
            Index = index,
            Point = [point.X, point.Y, point.Z]
        });
        return index;
    }

    private static bool SamePoint(double[] point, Point candidate)
    {
        const double epsilon = 0.0001;
        if (point.Length < 3)
            return false;

        return System.Math.Abs(point[0] - candidate.X) <= epsilon
            && System.Math.Abs(point[1] - candidate.Y) <= epsilon
            && System.Math.Abs(point[2] - candidate.Z) <= epsilon;
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
    private static double[] ToArray(Vector? vector) => vector == null ? [] : [vector.X, vector.Y, vector.Z];
}
