using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing.Dimensions.Defects;

/// <summary>
/// Reads the live inputs once and runs <see cref="DimensionDefectDetector"/> over the resulting
/// view snapshot. The detector stays pure; this class is only its Tekla-backed data source.
/// </summary>
public sealed class TeklaDrawingDimensionDefectApi
{
    private readonly Func<int, GetDimensionContextsResult> _getDimensionContexts;
    private readonly Func<int, List<PartGeometryInViewResult>> _getParts;
    private readonly Func<int, int, GetPartCandidatePointsResult> _getCandidates;

    public TeklaDrawingDimensionDefectApi(Model model)
    {
        var dimensionsApi = new TeklaDrawingDimensionsApi();
        var partGeometryApi = new TeklaDrawingPartGeometryApi(model);
        var candidatePointApi = new TeklaDrawingPartCandidatePointApi(model);

        _getDimensionContexts = dimensionsApi.GetDimensionContexts;
        _getParts = partGeometryApi.GetAllPartsGeometryInView;
        _getCandidates = candidatePointApi.GetPartCandidatePointsInView;
    }

    internal TeklaDrawingDimensionDefectApi(
        Func<int, GetDimensionContextsResult> getDimensionContexts,
        Func<int, List<PartGeometryInViewResult>> getParts,
        Func<int, int, GetPartCandidatePointsResult> getCandidates)
    {
        _getDimensionContexts = getDimensionContexts;
        _getParts = getParts;
        _getCandidates = getCandidates;
    }

    public DimensionDefectReport GetDimensionDefects(int viewId)
    {
        var contexts = _getDimensionContexts(viewId);
        var eligibleDimensions = contexts.Dimensions
            .Where(static dimension => dimension.PointAssociations.Count >= 2)
            .ToList();

        if (eligibleDimensions.Count == 0)
        {
            var emptyReport = DimensionDefectDetector.Detect(viewId, contexts.Dimensions, [], []);
            AddSourceWarnings(emptyReport, contexts.Warnings, []);
            return emptyReport;
        }

        var parts = _getParts(viewId) ?? [];
        if (parts.Count == 0)
        {
            var noPartsReport = DimensionDefectDetector.Detect(viewId, contexts.Dimensions, parts, []);
            AddSourceWarnings(noPartsReport, contexts.Warnings, []);
            return noPartsReport;
        }

        var candidatesByModelId = new Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>>();
        var sourceWarnings = new List<string>();
        var candidateReadFailed = false;

        foreach (var part in parts.GroupBy(static part => part.ModelId).Select(static group => group.First()))
        {
            var candidateResult = _getCandidates(viewId, part.ModelId);
            if (!candidateResult.Success)
            {
                candidateReadFailed = true;
                sourceWarnings.Add($"candidates_unavailable:{part.ModelId}" +
                    (string.IsNullOrWhiteSpace(candidateResult.Error) ? string.Empty : $":{candidateResult.Error}"));
                continue;
            }

            // Anchor confidence depends on the solid traversal that produced these candidates —
            // that read is authoritative, and overwrites whatever the separate geometry read
            // reported. ANDing the two would let an incomplete geometry read downgrade a real
            // PhantomAnchor to AnchorUnverified even when the read that actually produced the
            // candidates was complete.
            part.SolidGeometryComplete = candidateResult.SolidGeometryComplete;
            if (!candidateResult.SolidGeometryComplete)
                sourceWarnings.Add($"solid_geometry_incomplete:{part.ModelId}");

            candidatesByModelId[part.ModelId] = candidateResult.Candidates;
        }

        IReadOnlyList<DimensionChainCoverageResult> coverage;
        if (candidateReadFailed)
        {
            // A missing match cannot prove an unanchored point while any visible part was absent
            // from the candidate search. Supplying no coverage makes the unchanged detector report
            // that anchor checks did not run, while its other checks still remain available.
            coverage = [];
        }
        else
        {
            coverage = eligibleDimensions
                .Select(dimension => DimensionChainCoverageBuilder.Build(
                    dimension,
                    candidatesByModelId,
                    TeklaDimensionChainCoverageApi.DefaultToleranceMm))
                .ToList();
        }

        var report = DimensionDefectDetector.Detect(viewId, contexts.Dimensions, parts, coverage);
        AddSourceWarnings(report, contexts.Warnings, sourceWarnings);
        return report;
    }

    private static void AddSourceWarnings(
        DimensionDefectReport report,
        IEnumerable<string> contextWarnings,
        IEnumerable<string> sourceWarnings)
    {
        var additions = contextWarnings.Select(static warning => "dimension_context:" + warning)
            .Concat(sourceWarnings)
            .Where(static warning => !string.IsNullOrWhiteSpace(warning));

        foreach (var warning in additions)
            if (!report.Warnings.Contains(warning, StringComparer.Ordinal))
                report.Warnings.Add(warning);
    }
}
