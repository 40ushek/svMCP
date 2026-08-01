using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Read-only, one-view payload combining the drawing identity with the two existing
/// context payloads. It is intentionally an in-memory transport object; persistence
/// and example storage are separate concerns.
/// </summary>
public sealed class DimensionObservationResult
{
    public DimensionObservationHeader Header { get; set; } = new();
    public GetDrawingViewContextResult ViewContext { get; set; } = new();
    public GetDimensionContextsResult DimensionContext { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class DimensionObservationHeader
{
    public int SchemaVersion { get; set; } = 1;
    public string CapturedAtUtc { get; set; } = string.Empty;
    public string Units { get; set; } = "mm";
    public string TeklaVersion { get; set; } = string.Empty;
    public string AssemblyMark { get; set; } = string.Empty;
    public string AssemblyPrefix { get; set; } = string.Empty;
    public DrawingInfo Drawing { get; set; } = new();
    public string PartsPayloadHash { get; set; } = string.Empty;
    public string PartsPayloadEncoding { get; set; } = "json-utf8";
}

/// <summary>Builds an observation by reusing the existing view and dimension readers.</summary>
public sealed class TeklaDrawingDimensionObservationApi
{
    private readonly Model _model;

    public TeklaDrawingDimensionObservationApi()
        : this(new Model())
    {
    }

    public TeklaDrawingDimensionObservationApi(Model model)
    {
        _model = model ?? new Model();
    }

    public DimensionObservationResult Capture(int viewId)
    {
        var drawing = new TeklaDrawingQueryApi().GetActiveDrawingInfo();
        if (drawing == null)
            throw new DrawingNotOpenException();

        var viewContext = new TeklaDrawingViewContextApi(_model).GetViewContext(viewId);
        if (!viewContext.Success)
            throw new ViewNotFoundException(viewId);

        var dimensionContext = new TeklaDrawingDimensionsApi(_model).GetDimensionContexts(viewId);
        var warnings = new List<string>();
        warnings.AddRange(viewContext.Warnings);
        warnings.AddRange(dimensionContext.Warnings);

        var assemblyPrefix = ReadAssemblyPrefix(drawing, warnings);
        return new DimensionObservationResult
        {
            Header = new DimensionObservationHeader
            {
                CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                TeklaVersion = typeof(DrawingHandler).Assembly.GetName().Version?.ToString() ?? string.Empty,
                AssemblyMark = drawing.Mark ?? string.Empty,
                AssemblyPrefix = assemblyPrefix,
                Drawing = drawing
            },
            ViewContext = viewContext,
            DimensionContext = dimensionContext,
            Warnings = DistinctWarnings(warnings)
        };
    }

    private string ReadAssemblyPrefix(DrawingInfo drawing, ICollection<string> warnings)
    {
        if (!string.Equals(drawing.SourceModelObjectKind, "Assembly", StringComparison.OrdinalIgnoreCase)
            || !drawing.SourceModelObjectId.HasValue)
        {
            warnings.Add("assembly_prefix_unavailable");
            return string.Empty;
        }

        var modelObject = _model.SelectModelObject(new Identifier(drawing.SourceModelObjectId.Value));
        if (modelObject == null)
        {
            warnings.Add("assembly_prefix_model_object_unavailable");
            return string.Empty;
        }

        var prefix = string.Empty;
        if (!modelObject.GetReportProperty("ASSEMBLY_PREFIX", ref prefix) || string.IsNullOrWhiteSpace(prefix))
            warnings.Add("assembly_prefix_unavailable");

        return prefix ?? string.Empty;
    }

    private static List<string> DistinctWarnings(IEnumerable<string> warnings)
    {
        var result = new List<string>();
        foreach (var warning in warnings)
        {
            if (!string.IsNullOrWhiteSpace(warning) && !result.Contains(warning, StringComparer.Ordinal))
                result.Add(warning);
        }

        return result;
    }
}
