using System.Collections.Generic;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;
using SolidTypes = Tekla.Structures.Solid;

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
            var objEnum = view.GetObjects();
            while (objEnum.MoveNext())
            {
                if (objEnum.Current is not Tekla.Structures.Drawing.Part drawingPart)
                    continue;
                if (drawingPart.Hideable.IsHidden)
                    continue;

                var id = drawingPart.ModelIdentifier;
                var modelObj = _model.SelectModelObject(id);
                if (modelObj == null) continue;
                modelObj.Select(); // required for GetReportProperty to work

                var modelId = id.ID;
                double[] startPt = [], endPt = [], axisX = [], axisY = [], csOrigin = [];

                string typeName = modelObj.GetType().Name;
                string name = string.Empty, partPos = string.Empty, profile = string.Empty, material = string.Empty;
                double[] bboxMin = [], bboxMax = [];
                List<double[]> solidVertices = new();

                if (modelObj is Beam beam)
                {
                    startPt  = ToArray(beam.StartPoint);
                    endPt    = ToArray(beam.EndPoint);
                    var cs   = beam.GetCoordinateSystem();
                    csOrigin = ToArray(cs.Origin);
                    axisX    = ToArray(cs.AxisX);
                    axisY    = ToArray(cs.AxisY);
                    name     = beam.Name;
                    profile  = beam.Profile.ProfileString;
                    material = beam.Material.MaterialString;
                    var solid = beam.GetSolid();
                    if (solid != null)
                    {
                        bboxMin = ToArray(solid.MinimumPoint);
                        bboxMax = ToArray(solid.MaximumPoint);
                        solidVertices = CollectSolidVertices(solid);
                    }
                }
                else if (modelObj is ModelPart part)
                {
                    var cs   = part.GetCoordinateSystem();
                    startPt  = ToArray(cs.Origin);
                    csOrigin = ToArray(cs.Origin);
                    axisX    = ToArray(cs.AxisX);
                    axisY    = ToArray(cs.AxisY);
                    name     = part.Name;
                    var solid = part.GetSolid();
                    if (solid != null)
                    {
                        bboxMin = ToArray(solid.MinimumPoint);
                        bboxMax = ToArray(solid.MaximumPoint);
                        solidVertices = CollectSolidVertices(solid);
                    }
                    part.GetReportProperty("PROFILE",  ref profile);
                    part.GetReportProperty("MATERIAL", ref material);
                }

                modelObj.GetReportProperty("PART_POS", ref partPos);
                int materialType = -1;
                modelObj.GetReportProperty("MATERIAL_TYPE", ref materialType);
                if (materialType == -1)
                    materialType = InferMaterialType(material);

                results.Add(new PartGeometryInViewResult
                {
                    Success    = true,
                    ViewId     = viewId,
                    ModelId    = modelId,
                    StartPoint = startPt,
                    EndPoint   = endPt,
                    CoordinateSystemOrigin = csOrigin,
                    AxisX      = axisX,
                    AxisY      = axisY,
                    BboxMin    = bboxMin,
                    BboxMax    = bboxMax,
                    SolidVertices = solidVertices,
                    Type       = typeName,
                    Name       = name,
                    PartPos    = partPos,
                    Profile    = profile,
                    Material   = material,
                    MaterialType = materialType
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
            double[] csOrigin = [];

            if (modelObj is Beam beam)
            {
                startPt = ToArray(beam.StartPoint);
                endPt   = ToArray(beam.EndPoint);
                var cs  = beam.GetCoordinateSystem();
                csOrigin = ToArray(cs.Origin);
                axisX   = ToArray(cs.AxisX);
                axisY   = ToArray(cs.AxisY);
            }
            else if (modelObj is ModelPart part)
            {
                var cs = part.GetCoordinateSystem();
                startPt = ToArray(cs.Origin);
                csOrigin = ToArray(cs.Origin);
                axisX   = ToArray(cs.AxisX);
                axisY   = ToArray(cs.AxisY);
            }

            double[] bboxMin = [];
            double[] bboxMax = [];
            List<double[]> solidVertices = new();
            if (modelObj is ModelPart solidPart)
            {
                var solid = solidPart.GetSolid();
                if (solid != null)
                {
                    bboxMin = ToArray(solid.MinimumPoint);
                    bboxMax = ToArray(solid.MaximumPoint);
                    solidVertices = CollectSolidVertices(solid);
                }
            }

            return new PartGeometryInViewResult
            {
                Success    = true,
                ViewId     = viewId,
                ModelId    = modelId,
                StartPoint = startPt,
                EndPoint   = endPt,
                CoordinateSystemOrigin = csOrigin,
                AxisX      = axisX,
                AxisY      = axisY,
                BboxMin    = bboxMin,
                BboxMax    = bboxMax
                ,
                SolidVertices = solidVertices
            };
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }
    }

    private static PartGeometryInViewResult Fail(int viewId, int modelId, string error) =>
        new() { Success = false, ViewId = viewId, ModelId = modelId, Error = error };

    private static List<double[]> CollectSolidVertices(Solid solid)
    {
        var result = new List<double[]>();

        try
        {
            var faceEnumerator = solid.GetFaceEnumerator();
            while (faceEnumerator.MoveNext())
            {
                if (faceEnumerator.Current is not SolidTypes.Face face)
                    continue;

                var loopEnumerator = face.GetLoopEnumerator();
                while (loopEnumerator.MoveNext())
                {
                    if (loopEnumerator.Current is not SolidTypes.Loop loop)
                        continue;

                    if (loop.GetVertexEnumerator() is not SolidTypes.VertexEnumerator vertexEnumerator)
                        continue;

                    while (vertexEnumerator.MoveNext())
                    {
                        if (vertexEnumerator.Current is not Point vertex)
                            continue;

                        AddUniquePoint(result, vertex);
                    }
                }
            }
        }
        catch
        {
            // Some runtime solids may not expose stable face/loop traversal.
        }

        return result;
    }

    private static void AddUniquePoint(List<double[]> target, Point point)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (SamePoint(target[i], point))
                return;
        }

        target.Add([R(point.X), R(point.Y), R(point.Z)]);
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

    private static double[] ToArray(Point? p)  => p  == null ? [] : [R(p.X), R(p.Y), R(p.Z)];
    private static double[] ToArray(Vector? v) => v  == null ? [] : [R(v.X), R(v.Y), R(v.Z)];
    private static double R(double v) => System.Math.Round(v, 5);

    /// <summary>
    /// Infers Tekla MATERIAL_TYPE (1=Steel, 2=Concrete, 5=Timber, 6=Misc) from material name string.
    /// Used as fallback when GetReportProperty("MATERIAL_TYPE") returns -1.
    /// </summary>
    private static int InferMaterialType(string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return -1;
        var s = materialName.Trim().ToUpperInvariant();

        // Misc / insulation — check first before any C-grade match
        if (s.Contains("WOOL") || s.Contains("GLASS") || s.Contains("FOAM") ||
            s.Contains("MINERAL") || s.Contains("INSUL") || s.Contains("GIPS") ||
            s.Contains("GYPS") || s.Contains("EPS") || s.Contains("XPS") ||
            s.Contains("FOIL") || s.Contains("FOLIE") || s.Contains("BITUM"))
            return 6;

        // Concrete: C<digits>/<digits>  e.g. C20/25, C30/37
        if (s.Length > 2 && s[0] == 'C' && char.IsDigit(s[1]) && s.Contains('/'))
            return 2;

        // Timber: C<digits> (no slash)  e.g. C24, C18; GL*, KVH*, BSH*, LVL*
        if ((s.Length > 1 && s[0] == 'C' && char.IsDigit(s[1]) && !s.Contains('/')) ||
            s.StartsWith("GL") || s.StartsWith("KVH") || s.StartsWith("BSH") || s.StartsWith("LVL"))
            return 5;

        // Steel: S<digits>, Fe*, A3*, A5*, HE*, IPE*, RHS*, SHS*, CHS*, etc.
        if ((s.Length > 1 && s[0] == 'S' && char.IsDigit(s[1])) ||
            s.StartsWith("FE") || s.StartsWith("HE") || s.StartsWith("IPE") ||
            s.StartsWith("RHS") || s.StartsWith("SHS") || s.StartsWith("CHS"))
            return 1;

        return -1;
    }
}
