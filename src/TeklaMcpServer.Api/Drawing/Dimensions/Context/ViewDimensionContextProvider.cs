using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One active drawing/view run, with separate snapshots for each exclusion scope.</summary>
public sealed class ViewDimensionContextProvider
{
    private readonly Func<string?> _drawingIdentity;
    private readonly Func<int, IReadOnlyList<PartExclusionRule>, ViewDimensionContext> _read;
    private readonly Action _invalidateGeometry;
    private readonly Func<CreateDimensionRequest, double, CreateDimensionResult> _write;
    private readonly Func<int, int, PartSolidGeometryInViewResult> _readContactSolid;
    private readonly Dictionary<string, ViewDimensionContext> _contexts = new();
    private ViewContactsSnapshot? _contacts;
    private string? _drawing;
    private int? _view;

    public ViewDimensionContextProvider(Model model)
    {
        var reader = new TeklaViewDimensionContextReader(model);
        _drawingIdentity = reader.ActiveIdentity;
        _read = reader.Read;
        _readContactSolid = reader.ReadPartSolidGeometry;
        _invalidateGeometry = DrawingPartGeometryCache.InvalidateAll;
        _write = Write;
    }

    internal ViewDimensionContextProvider(Func<string?> drawingIdentity,
        Func<int, IReadOnlyList<PartExclusionRule>, ViewDimensionContext> read, Action invalidateGeometry,
        Func<CreateDimensionRequest, double, CreateDimensionResult>? write = null,
        Func<int, int, PartSolidGeometryInViewResult>? readContactSolid = null)
    {
        _drawingIdentity = drawingIdentity;
        _read = read;
        _invalidateGeometry = invalidateGeometry;
        _write = write ?? Write;
        _readContactSolid = readContactSolid ?? ((viewId, modelId) =>
            throw new InvalidOperationException("Contact solid reader is unavailable"));
    }

    public void ObserveActiveDrawing()
    {
        var current = _drawingIdentity();
        if (current == _drawing) return;
        Clear();
        _drawing = current;
        _view = null;
    }

    public ViewDimensionContext Get(int viewId, string? excludePrefixes = null,
        string? excludeMaterials = null, bool refresh = false)
    {
        ObserveActiveDrawing();
        if (_drawing == null) throw new InvalidOperationException("No drawing is currently open");
        if (refresh)
        {
            Clear();
        }
        ObserveView(viewId);
        var rules = Normalize(excludePrefixes, excludeMaterials);
        var key = JsonSerializer.Serialize(rules.Select(r => new { r.Kind, r.Value }));
        if (_contexts.TryGetValue(key, out var cached))
        {
            PerfTrace.Write("api-geometry", "dimension_context_hit", 0, $"viewId={viewId}");
            AttachContacts(cached);
            return cached;
        }
        // Failed builds never enter the store. A later call can retry reading.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var context = _read(viewId, rules);
        _contexts.Add(key, context);
        AttachContacts(context);
        PerfTrace.Write("api-geometry", "dimension_context_build", timer.ElapsedMilliseconds, $"viewId={viewId}");
        return context;
    }

    public void ObserveView(int viewId)
    {
        if (_view == viewId) return;
        Clear();
        _view = viewId;
    }

    public CreateDimensionResult Create(CreateDimensionRequest request)
    {
        ViewDimensionContext context;
        if (request.PointIds.Length > 0 || !string.IsNullOrWhiteSpace(request.ContextId))
        {
            if (string.IsNullOrWhiteSpace(request.ContextId) || request.PointIds.Length == 0)
                throw new ArgumentException("contextId and pointIds must be supplied together");
            if (request.Points.Length > 0)
                throw new ArgumentException("Supply either coordinates or contextId with pointIds, not both");
            if (!string.IsNullOrWhiteSpace(request.ExcludePrefixes) || !string.IsNullOrWhiteSpace(request.ExcludeMaterials))
                throw new ArgumentException("Exclusion filters are part of contextId; do not pass filters with point ids");

            ObserveActiveDrawing();
            if (_drawing == null) throw new InvalidOperationException("No drawing is currently open");
            ObserveView(request.ViewId);
            context = _contexts.Values.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.ContextId, request.ContextId))
                ?? throw new InvalidOperationException("Unknown or expired contextId; refresh the view context and retry");
            if (!context.IsComplete)
                throw new InvalidOperationException("Cannot create a dimension from pointIds in an incomplete view context; inspect diagnostics and refresh after fixing the source");
            request.Points = context.ResolvePointIds(request.PointIds, request.Direction);
        }
        else
        {
            context = request.Distance.HasValue ? null! : Get(request.ViewId, request.ExcludePrefixes, request.ExcludeMaterials);
        }

        var placement = request.Distance.HasValue ? null
            : context.Calculate(request.Direction, request.Points, request.PaperGapMm);
        var distance = request.Distance ?? placement!.Distance;
        var result = _write(request, distance);
        result.DistanceUsed = distance;
        result.Placement = placement;
        return result;
    }

    private static CreateDimensionResult Write(CreateDimensionRequest request, double distance) =>
        new TeklaDrawingDimensionsApi().CreateDimension(request.ViewId, request.Points,
            request.Direction, distance, request.AttributesFile);

    private void Clear()
    {
        _contexts.Clear();
        _contacts = null;
        _invalidateGeometry();
    }

    private void AttachContacts(ViewDimensionContext context)
    {
        if (_contacts == null)
            _contacts = new ViewContactsSnapshot(context.ViewId, context.ContactModelIds,
                context.ContactSelectionUnread, modelId => _readContactSolid(context.ViewId, modelId));
        context.AttachContacts(_contacts);
    }

    internal static IReadOnlyList<PartExclusionRule> Normalize(string? prefixes, string? materials) =>
        PartExclusions.Parse(prefixes, materials)
            .Select(r => new { r.Kind, Value = r.Value.ToUpperInvariant() })
            .Distinct().OrderBy(r => r.Kind).ThenBy(r => r.Value, StringComparer.Ordinal)
            .Select(r => r.Kind == PartExclusionKind.Prefix
                ? PartExclusionRule.ByPrefix(r.Value) : PartExclusionRule.ByMaterial(r.Value)).ToArray();
}

internal sealed class TeklaViewDimensionContextReader(Model model)
{
    public string? ActiveIdentity()
    {
        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null) return null;
        var id = drawing.GetIdentifier();
        return model.GetInfo().ModelPath + "|" + id.GUID + "|" + id.ID;
    }

    public ViewDimensionContext Read(int viewId, IReadOnlyList<PartExclusionRule> exclusions)
    {
        var drawing = new DrawingHandler().GetActiveDrawing()
            ?? throw new InvalidOperationException("No drawing is currently open");
        var view = DrawingViewParts.FindView(drawing, viewId) ?? throw new ViewNotFoundException(viewId);
        if (!view.Select()) throw new InvalidOperationException("Cannot select view to capture dimension context");
        var cs = view.ViewCoordinateSystem;
        var display = view.DisplayCoordinateSystem;
        var scale = view.Attributes.Scale;
        var depth = ViewDepthWindow.Read(model, view);
        var outline = new TeklaDrawingStructuralOutlineApi(
            new TeklaDrawingPartRoleApi(model, new PartRoleClassifier(exclusions)),
            new TeklaDrawingAssemblyOutlineApi(model)).Get(viewId);
        var identities = outline.Included.Select(p => new { ModelId = p.ModelId, Guid = ObjectGuid(p.ModelId) }).ToArray();
        var assemblyGuid = drawing is AssemblyDrawing assembly && assembly.AssemblyIdentifier != null
            ? ObjectGuid(assembly.AssemblyIdentifier.ID) : null;
        var unresolved = identities.Where(p => p.Guid.Length == 0).Select(p => p.ModelId).ToArray();
        var drawingGuid = drawing.GetIdentifier().GUID;
        var source = new {
            drawingGuid = drawingGuid == Guid.Empty ? "" : drawingGuid.ToString(),
            drawingMark = drawing.Mark, drawingName = drawing.Name,
            drawingModified = drawing.ModificationDate.ToString("O", CultureInfo.InvariantCulture),
            assemblyGuid, fromActiveSheetOnly = false,
            isComplete = drawingGuid != Guid.Empty && unresolved.Length == 0
                && (drawing is not AssemblyDrawing || !string.IsNullOrEmpty(assemblyGuid)),
            partsLabel = "included", parts = identities.Where(p => p.Guid.Length > 0).ToArray(),
            unresolvedPartIds = unresolved
        };
        var metadata = new {
            viewId, viewType = view.ViewType.ToString(), scale,
            viewCoordinates = new[] { cs.Origin.X, cs.Origin.Y, cs.Origin.Z, cs.AxisX.X, cs.AxisX.Y, cs.AxisX.Z, cs.AxisY.X, cs.AxisY.Y, cs.AxisY.Z },
            displayCoordinates = new[] { display.Origin.X, display.Origin.Y, display.Origin.Z, display.AxisX.X, display.AxisX.Y, display.AxisX.Z, display.AxisY.X, display.AxisY.Y, display.AxisY.Z },
            depthWindow = depth.Box, depthError = depth.Error
        };
        return new ViewDimensionContext(viewId, scale, outline, exclusions, source, metadata);
    }

    public PartSolidGeometryInViewResult ReadPartSolidGeometry(int viewId, int modelId) =>
        new TeklaDrawingPartSolidGeometryApi(model).GetPartSolidGeometryInView(viewId, modelId);

    private string ObjectGuid(int modelId)
    {
        try {
            var obj = model.SelectModelObject(new Tekla.Structures.Identifier(modelId));
            var guid = obj?.Identifier.GUID ?? Guid.Empty;
            return guid == Guid.Empty ? "" : guid.ToString();
        }
        catch { return ""; }
    }
}
