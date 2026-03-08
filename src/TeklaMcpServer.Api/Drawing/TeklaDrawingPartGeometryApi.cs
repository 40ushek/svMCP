using System.Collections.Generic;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingPartGeometryApi : IDrawingPartGeometryApi
{
    private readonly Model _model;

    public TeklaDrawingPartGeometryApi(Model model)
    {
        _model = model;
    }

    public List<PartGeometryInViewResult> GetAllPartsGeometryInView(int viewId)
    {
        var dh = new DrawingHandler();
        var activeDrawing = dh.GetActiveDrawing();
        if (activeDrawing == null)
            return new List<PartGeometryInViewResult>();

        View? view = null;
        var viewEnum = activeDrawing.GetSheet().GetViews();
        while (viewEnum.MoveNext())
        {
            if (viewEnum.Current is View v && v.GetIdentifier().ID == viewId)
            {
                view = v;
                break;
            }
        }
        if (view == null)
            return new List<PartGeometryInViewResult>();

        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane    = workPlaneHandler.GetCurrentTransformationPlane();
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(view.DisplayCoordinateSystem));

        var results = new List<PartGeometryInViewResult>();
        try
        {
            var identifiers = dh.GetModelObjectIdentifiers(activeDrawing);
            foreach (Tekla.Structures.Identifier id in identifiers)
            {
                var modelObj = _model.SelectModelObject(id);
                if (modelObj == null) continue;

                var modelId = id.ID;
                double[] startPt = [], endPt = [], axisX = [], axisY = [];

                if (modelObj is Beam beam)
                {
                    startPt = ToArray(beam.StartPoint);
                    endPt   = ToArray(beam.EndPoint);
                    var cs  = beam.GetCoordinateSystem();
                    axisX   = ToArray(cs.AxisX);
                    axisY   = ToArray(cs.AxisY);
                }
                else if (modelObj is ModelPart part)
                {
                    var cs = part.GetCoordinateSystem();
                    startPt = ToArray(cs.Origin);
                    axisX   = ToArray(cs.AxisX);
                    axisY   = ToArray(cs.AxisY);
                }

                double[] bboxMin = [], bboxMax = [];
                if (modelObj is ModelPart solidPart)
                {
                    var solid = solidPart.GetSolid();
                    if (solid != null)
                    {
                        bboxMin = ToArray(solid.MinimumPoint);
                        bboxMax = ToArray(solid.MaximumPoint);
                    }
                }

                string typeName = modelObj.GetType().Name;
                string name = string.Empty, partPos = string.Empty, profile = string.Empty, material = string.Empty;
                modelObj.GetReportProperty("NAME",     ref name);
                modelObj.GetReportProperty("PART_POS", ref partPos);
                modelObj.GetReportProperty("PROFILE",  ref profile);
                modelObj.GetReportProperty("MATERIAL", ref material);

                results.Add(new PartGeometryInViewResult
                {
                    Success    = true,
                    ViewId     = viewId,
                    ModelId    = modelId,
                    StartPoint = startPt,
                    EndPoint   = endPt,
                    AxisX      = axisX,
                    AxisY      = axisY,
                    BboxMin    = bboxMin,
                    BboxMax    = bboxMax,
                    Type       = typeName,
                    Name       = name,
                    PartPos    = partPos,
                    Profile    = profile,
                    Material   = material
                });
            }
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }
        return results;
    }

    public PartGeometryInViewResult GetPartGeometryInView(int viewId, int modelId)
    {
        var dh = new DrawingHandler();
        var activeDrawing = dh.GetActiveDrawing();
        if (activeDrawing == null)
            return Fail(viewId, modelId, "No drawing is currently open.");

        // Find the view
        View? view = null;
        var viewEnum = activeDrawing.GetSheet().GetViews();
        while (viewEnum.MoveNext())
        {
            if (viewEnum.Current is View v && v.GetIdentifier().ID == viewId)
            {
                view = v;
                break;
            }
        }
        if (view == null)
            return Fail(viewId, modelId, $"View {viewId} not found in active drawing.");

        // Pattern from ObjectDimensioningCreator:
        // Set work plane to view's DisplayCoordinateSystem so that all model
        // coordinates are returned in view-local space.
        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane    = workPlaneHandler.GetCurrentTransformationPlane();
        var displayCS        = view.DisplayCoordinateSystem;

        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(displayCS));
        try
        {
            var identifier = new Identifier(modelId);
            var modelObj   = _model.SelectModelObject(identifier);
            if (modelObj == null)
                return Fail(viewId, modelId, $"Model object {modelId} not found.");

            modelObj.Select();

            double[] startPt = [];
            double[] endPt   = [];
            double[] axisX   = [];
            double[] axisY   = [];

            if (modelObj is Beam beam)
            {
                startPt = ToArray(beam.StartPoint);
                endPt   = ToArray(beam.EndPoint);
                var cs  = beam.GetCoordinateSystem();
                axisX   = ToArray(cs.AxisX);
                axisY   = ToArray(cs.AxisY);
            }
            else if (modelObj is ModelPart part)
            {
                var cs = part.GetCoordinateSystem();
                startPt = ToArray(cs.Origin);
                axisX   = ToArray(cs.AxisX);
                axisY   = ToArray(cs.AxisY);
            }

            double[] bboxMin = [];
            double[] bboxMax = [];
            if (modelObj is ModelPart solidPart)
            {
                var solid = solidPart.GetSolid();
                if (solid != null)
                {
                    bboxMin = ToArray(solid.MinimumPoint);
                    bboxMax = ToArray(solid.MaximumPoint);
                }
            }

            return new PartGeometryInViewResult
            {
                Success    = true,
                ViewId     = viewId,
                ModelId    = modelId,
                StartPoint = startPt,
                EndPoint   = endPt,
                AxisX      = axisX,
                AxisY      = axisY,
                BboxMin    = bboxMin,
                BboxMax    = bboxMax
            };
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }
    }

    private static PartGeometryInViewResult Fail(int viewId, int modelId, string error) =>
        new() { Success = false, ViewId = viewId, ModelId = modelId, Error = error };

    private static double[] ToArray(Point? p)  => p  == null ? [] : [p.X,  p.Y,  p.Z];
    private static double[] ToArray(Vector? v) => v  == null ? [] : [v.X,  v.Y,  v.Z];
}
