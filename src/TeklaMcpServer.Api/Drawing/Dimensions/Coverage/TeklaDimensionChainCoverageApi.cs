using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Reads one chain and the candidate points of every part in its view, then joins them.
/// Both sides come from existing readers, so the coverage result can be re-derived from a saved
/// dimension context and saved candidates without touching Tekla again.
/// </summary>
public sealed class TeklaDimensionChainCoverageApi
{
    /// <summary>
    /// Default match radius. Chosen to be well below the shortest spacing that has to survive
    /// on these drawings — the junk segments seen in practice were 15 mm and up — while staying
    /// above snap noise.
    /// </summary>
    public const double DefaultToleranceMm = 1.0;

    private readonly TeklaDrawingDimensionsApi _dimensionsApi;
    private readonly IDrawingPartGeometryApi _partGeometryApi;
    private readonly IDrawingPartCandidatePointApi _candidatePointApi;

    public TeklaDimensionChainCoverageApi(Model model)
        : this(
            new TeklaDrawingDimensionsApi(),
            new TeklaDrawingPartGeometryApi(model),
            new TeklaDrawingPartCandidatePointApi(model))
    {
    }

    internal TeklaDimensionChainCoverageApi(
        TeklaDrawingDimensionsApi dimensionsApi,
        IDrawingPartGeometryApi partGeometryApi,
        IDrawingPartCandidatePointApi candidatePointApi)
    {
        _dimensionsApi = dimensionsApi;
        _partGeometryApi = partGeometryApi;
        _candidatePointApi = candidatePointApi;
    }

    public DimensionChainCoverageResult GetDimensionChainCoverage(int viewId, int dimensionId, double tolerance)
    {
        var contexts = _dimensionsApi.GetDimensionContexts(viewId);
        var dimension = contexts.Dimensions.FirstOrDefault(context => context.DimensionId == dimensionId);
        if (dimension == null)
        {
            return new DimensionChainCoverageResult
            {
                Success = false,
                ViewId = viewId,
                DimensionId = dimensionId,
                Tolerance = tolerance,
                Error = $"Dimension {dimensionId} was not found in view {viewId}."
            };
        }

        var candidatesByModelId = new Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>>();
        var warnings = new List<string>();

        foreach (var part in _partGeometryApi.GetAllPartsGeometryInView(viewId))
        {
            if (candidatesByModelId.ContainsKey(part.ModelId))
                continue;

            var candidates = _candidatePointApi.GetPartCandidatePointsInView(viewId, part.ModelId);
            if (!candidates.Success)
            {
                warnings.Add($"candidates_unavailable:{part.ModelId}");
                continue;
            }

            if (!candidates.SolidGeometryComplete)
                warnings.Add($"solid_geometry_incomplete:{part.ModelId}");

            candidatesByModelId[part.ModelId] = candidates.Candidates;
        }

        var result = DimensionChainCoverageBuilder.Build(dimension, candidatesByModelId, tolerance);
        result.Warnings.InsertRange(0, warnings);
        return result;
    }
}
