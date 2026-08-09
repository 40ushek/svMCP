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

    /// <summary>
    /// The part's own coordinate system, in whatever plane is current. Empty arrays rather
    /// than a failure when the part has none: the geometry is still worth returning, and a
    /// caller can fall back to the view axes for a looser bounding box.
    /// </summary>
    private static (double[] Origin, double[] AxisX, double[] AxisY) ReadCoordinateSystem(ModelPart part)
    {
        try
        {
            var cs = part.GetCoordinateSystem();
            if (cs == null)
                return ([], [], []);

            return (ToArray(cs.Origin), ToArray(cs.AxisX), ToArray(cs.AxisY));
        }
        catch
        {
            return ([], [], []);
        }
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

            // Beams at high accuracy, everything else at normal. A rougher solid on a cut
            // beam loses the very faces a contact would be found on, and reading the same
            // part at a different accuracy here than the model path uses would let one
            // part answer differently depending on which way it was asked.
            var solid = part is Beam
                ? part.GetSolid(Solid.SolidCreationTypeEnum.HIGH_ACCURACY)
                : part.GetSolid(Solid.SolidCreationTypeEnum.NORMAL);
            if (solid == null)
                return Fail(viewId, modelId, $"Model object {modelId} does not expose solid geometry.");

            // Read here, inside the view plane and with the part already in hand, so the
            // axes arrive in the same coordinates as the faces and cost no second lookup.
            var partPlane = ReadCoordinateSystem(part);

            return new PartSolidGeometryInViewResult
            {
                Success = true,
                ViewId = viewId,
                ModelId = modelId,
                StartPoint = part is Beam beam ? ToArray(beam.StartPoint) : [],
                EndPoint = part is Beam endPointBeam ? ToArray(endPointBeam.EndPoint) : [],
                CoordinateSystemOrigin = partPlane.Origin,
                AxisX = partPlane.AxisX,
                AxisY = partPlane.AxisY,
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

    private static double[] ToArray(Vector? vector) => vector == null ? [] : [vector.X, vector.Y, vector.Z];
}
