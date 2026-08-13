using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;
using System.Diagnostics;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingPartGeometryApi : IDrawingPartGeometryApi
{
    private static readonly PartRoleClassifier RoleClassifier = new();

    private readonly Model _model;

    public TeklaDrawingPartGeometryApi(Model model)
    {
        _model = model;
    }

    public List<PartInView> GetAllPartsGeometryInView(int viewId)
    {
        var total = Stopwatch.StartNew();
        var dh = new DrawingHandler();
        var activeDrawing = dh.GetActiveDrawing();
        if (activeDrawing == null)
            return new();

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
            return new();

        if (DrawingPartGeometryCache.TryGetAll(activeDrawing, view, viewId, out var cachedResults))
            return cachedResults;

        // See DrawingViewPlane: a view reporting the model system cannot be read in view
        // coordinates, and reading anyway returns confident nonsense.
        if (DrawingViewPlane.IsModelPlane(view.ViewCoordinateSystem))
            return new();

        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        var viewPlane = new TransformationPlane(view.ViewCoordinateSystem);
        workPlaneHandler.SetCurrentTransformationPlane(viewPlane);
        //_model.CommitChanges();

        var results = new List<PartInView>();
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
                var modelPart = _model.SelectModelObject(id) as ModelPart;
                if (modelPart == null) continue;
                modelPart.Select(); // required for GetReportProperty to work

                var modelId = id.ID;
                double[] startPt = [], endPt = [], axisX = [], axisY = [], csOrigin = [];

                string typeName = modelPart.GetType().Name;

                string name = string.Empty, partPos = string.Empty, profile = string.Empty, material = string.Empty;
                double[] bboxMin = [], bboxMax = [];
                List<double[]> solidVertices = new();
                List<double[]> viewHull = new();
                var solidGeometryComplete = false;

                if (modelPart is Beam beam)
                {

                    startPt = ToArray(beam.StartPoint);
                    endPt = ToArray(beam.EndPoint);
                    var cs = beam.GetCoordinateSystem();
                    csOrigin = ToArray(cs.Origin);
                    axisX = ToArray(cs.AxisX);
                    axisY = ToArray(cs.AxisY);
                    name = beam.Name;
                    profile = beam.Profile.ProfileString;
                    material = beam.Material.MaterialString;
                    var solid = beam.GetSolid();
                    if (solid != null)
                    {
                        bboxMin = ToArray(solid.MinimumPoint);
                        bboxMax = ToArray(solid.MaximumPoint);
                        var snapshot = SolidViewVertexCollector.Collect(solid);
                        solidGeometryComplete = snapshot.IsComplete;
                        if (solidGeometryComplete)
                        {
                            solidVertices = snapshot.Vertices;
                            viewHull = PartViewGeometryBuilder.BuildHull(snapshot.Vertices);
                        }
                    }

                    //var rect1 = new Rectangle(view, solid.MinimumPoint, solid.MaximumPoint);
                    //rect1.Attributes.Line.Color = DrawingColors.Magenta;
                    //rect1.Insert();

                }
                else if (modelPart is ModelPart part)
                {
                    var cs = part.GetCoordinateSystem();
                    startPt = ToArray(cs.Origin);
                    csOrigin = ToArray(cs.Origin);
                    axisX = ToArray(cs.AxisX);
                    axisY = ToArray(cs.AxisY);
                    name = part.Name;
                    var solid = part.GetSolid();
                    if (solid != null)
                    {
                        bboxMin = ToArray(solid.MinimumPoint);
                        bboxMax = ToArray(solid.MaximumPoint);
                        var snapshot = SolidViewVertexCollector.Collect(solid);
                        solidGeometryComplete = snapshot.IsComplete;
                        if (solidGeometryComplete)
                        {
                            solidVertices = snapshot.Vertices;
                            viewHull = PartViewGeometryBuilder.BuildHull(snapshot.Vertices);
                        }
                    }
                    part.GetReportProperty("PROFILE", ref profile);
                    part.GetReportProperty("MATERIAL", ref material);
                }

                modelPart.GetReportProperty("PART_POS", ref partPos);
                int materialType = -1;
                modelPart.GetReportProperty("MATERIAL_TYPE", ref materialType);
                if (materialType == -1)
                    materialType = InferMaterialType(material);

                string partPrefix = string.Empty;
                modelPart.GetReportProperty("PART_PREFIX", ref partPrefix);

                results.Add(new PartInView
                {
                    Success = true,
                    ViewId = viewId,
                    ModelId = modelId,
                    StartPoint = startPt,
                    EndPoint = endPt,
                    CoordinateSystemOrigin = csOrigin,
                    AxisX = axisX,
                    AxisY = axisY,
                    BboxMin = bboxMin,
                    BboxMax = bboxMax,
                    SolidVertices = solidVertices,
                    ViewHull = viewHull,
                    SolidGeometryComplete = solidGeometryComplete,
                    Type = typeName,
                    Name = name,
                    PartPos = partPos,
                    Profile = profile,
                    Material = material,
                    MaterialType = materialType,
                    PartPrefix = partPrefix,

                    // Classified here, where the properties it reads have just been read,
                    // so every consumer sees the same answer instead of working it out
                    // again and differently.
                    Role = RoleClassifier.ClassifyProperties(partPrefix, profile, material, materialType, name)
                });
            }
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
            PerfTrace.Write(
                "api-geometry",
                "get_all_parts_geometry_in_view_total",
                total.ElapsedMilliseconds,
                $"viewId={viewId} parts={results.Count}");
        }

        DrawingPartGeometryCache.StoreAll(activeDrawing, view, viewId, results);
        return results;
    }

    public PartInView GetPartGeometryInView(int viewId, int modelId)
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

        if (DrawingPartGeometryCache.TryGetPart(activeDrawing, view, viewId, modelId, out var cachedResult))
            return cachedResult;

        // Pattern from ObjectDimensioningCreator:
        // Set work plane to view's DisplayCoordinateSystem so that all model
        // coordinates are returned in view-local space.
        var viewCS = view.ViewCoordinateSystem;
        if (DrawingViewPlane.IsModelPlane(viewCS))
            return new PartInView { Success = false, ViewId = viewId, ModelId = modelId, Error = DrawingViewPlane.ModelPlaneReason };

        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(viewCS));
        //_model.CommitChanges();

        try
        {
            var identifier = new Identifier(modelId);
            var modelObj = _model.SelectModelObject(identifier);
            if (modelObj == null)
                return Fail(viewId, modelId, $"Model object {modelId} not found.");
            modelObj.Select();

            double[] startPt = [];
            double[] endPt = [];
            double[] axisX = [];
            double[] axisY = [];
            double[] csOrigin = [];

            if (modelObj is Beam beam)
            {
                startPt = ToArray(beam.StartPoint);
                endPt = ToArray(beam.EndPoint);
                var cs = beam.GetCoordinateSystem();
                csOrigin = ToArray(cs.Origin);
                axisX = ToArray(cs.AxisX);
                axisY = ToArray(cs.AxisY);
            }
            else if (modelObj is ModelPart part)
            {
                var cs = part.GetCoordinateSystem();
                startPt = ToArray(cs.Origin);
                csOrigin = ToArray(cs.Origin);
                axisX = ToArray(cs.AxisX);
                axisY = ToArray(cs.AxisY);
            }

            double[] bboxMin = [];
            double[] bboxMax = [];
            List<double[]> solidVertices = new();
            List<double[]> viewHull = new();
            var solidGeometryComplete = false;
            if (modelObj is ModelPart solidPart)
            {
                var solid = solidPart.GetSolid();
                if (solid != null)
                {
                    bboxMin = ToArray(solid.MinimumPoint);
                    bboxMax = ToArray(solid.MaximumPoint);
                    var snapshot = SolidViewVertexCollector.Collect(solid);
                    solidGeometryComplete = snapshot.IsComplete;
                    if (solidGeometryComplete)
                    {
                        solidVertices = snapshot.Vertices;
                        viewHull = PartViewGeometryBuilder.BuildHull(snapshot.Vertices);
                    }
                }
            }

            var result = new PartInView
            {
                Success = true,
                ViewId = viewId,
                ModelId = modelId,
                StartPoint = startPt,
                EndPoint = endPt,
                CoordinateSystemOrigin = csOrigin,
                AxisX = axisX,
                AxisY = axisY,
                BboxMin = bboxMin,
                BboxMax = bboxMax
                ,
                SolidVertices = solidVertices,
                ViewHull = viewHull,
                SolidGeometryComplete = solidGeometryComplete
            };
            DrawingPartGeometryCache.StorePart(activeDrawing, view, viewId, result);
            return result;
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
            //_model.CommitChanges();
        }
    }

    private static PartInView Fail(int viewId, int modelId, string error) =>
        new() { Success = false, ViewId = viewId, ModelId = modelId, Error = error };

    private static double[] ToArray(Point? p) => p == null ? [] : [R(p.X), R(p.Y), R(p.Z)];
    private static double[] ToArray(Vector? v) => v == null ? [] : [R(v.X), R(v.Y), R(v.Z)];
    private static double R(double v) => Math.Round(v, 5);

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
        if (s.Length > 1 && s[0] == 'C' && char.IsDigit(s[1]) && !s.Contains('/') ||
            s.StartsWith("GL") || s.StartsWith("KVH") || s.StartsWith("BSH") || s.StartsWith("LVL"))
            return 5;

        // Steel: S<digits>, Fe*, A3*, A5*, HE*, IPE*, RHS*, SHS*, CHS*, etc.
        if (s.Length > 1 && s[0] == 'S' && char.IsDigit(s[1]) ||
            s.StartsWith("FE") || s.StartsWith("HE") || s.StartsWith("IPE") ||
            s.StartsWith("RHS") || s.StartsWith("SHS") || s.StartsWith("CHS"))
            return 1;

        return -1;
    }
}
