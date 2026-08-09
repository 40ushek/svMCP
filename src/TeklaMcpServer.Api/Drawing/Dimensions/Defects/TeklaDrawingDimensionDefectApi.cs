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
    private readonly Func<int, ViewContactsResult>? _getContacts;

    public TeklaDrawingDimensionDefectApi(Model model)
    {
        var dimensionsApi = new TeklaDrawingDimensionsApi();
        var partGeometryApi = new TeklaDrawingPartGeometryApi(model);
        var candidatePointApi = new TeklaDrawingPartCandidatePointApi(model);
        var contactApi = new TeklaDrawingViewContactApi(model);

        _getDimensionContexts = dimensionsApi.GetDimensionContexts;
        _getParts = partGeometryApi.GetAllPartsGeometryInView;
        _getCandidates = candidatePointApi.GetPartCandidatePointsInView;
        _getContacts = viewId => contactApi.GetContactGraph(viewId);
    }

    internal TeklaDrawingDimensionDefectApi(
        Func<int, GetDimensionContextsResult> getDimensionContexts,
        Func<int, List<PartGeometryInViewResult>> getParts,
        Func<int, int, GetPartCandidatePointsResult> getCandidates,
        Func<int, ViewContactsResult>? getContacts = null)
    {
        _getDimensionContexts = getDimensionContexts;
        _getParts = getParts;
        _getCandidates = getCandidates;
        _getContacts = getContacts;
    }

    /// <summary>
    /// Above this many parts in a view, contacts are not searched unless asked for twice over.
    ///
    /// The search is every pair, and the box test that rejects most of them is cheap but not
    /// free. Measured on a real sheet: 56 parts, 1540 pairs, well under a second, and four such
    /// views in a couple of seconds. Ten times the parts is a hundred times the pairs, and this
    /// runs inside a defect scan people expect to be quick. The limit is set an order above what
    /// has actually been measured rather than at some round number, and the scan says when it
    /// skipped rather than going quiet.
    /// </summary>
    public const int ContactSearchPartLimit = 200;

    public DimensionDefectReport GetDimensionDefects(int viewId, bool withContacts = false)
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

        // Contacts are read here rather than inside the detector so the detector stays pure and
        // keeps running against the captured states, which carry no contacts. Where they are
        // absent the contact check simply does not run.
        SolidContacts.ContactGraph? contacts = null;

        if (withContacts && _getContacts != null && parts.Count > ContactSearchPartLimit)
        {
            sourceWarnings.Add(
                $"contacts not searched: {parts.Count} parts in the view is over the {ContactSearchPartLimit} limit, and the search is every pair");
        }
        else if (withContacts && _getContacts != null)
        {
            try
            {
                var viewContacts = _getContacts(viewId);
                if (viewContacts.IsComplete)
                    contacts = viewContacts.Graph;
                else
                    sourceWarnings.Add($"contacts not used: {viewContacts.Error ?? $"{viewContacts.Unread.Count} part(s) unread"}");
            }
            catch (Exception exception)
            {
                sourceWarnings.Add($"contacts not read: {exception.Message}");
            }
        }

        var report = DimensionDefectDetector.Detect(viewId, contexts.Dimensions, parts, coverage, contacts);
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
