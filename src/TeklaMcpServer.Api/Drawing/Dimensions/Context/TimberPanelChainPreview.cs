using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Read-only chain proposal for straight timber wall panels.</summary>
internal static class TimberPanelChainPreview
{
    internal sealed class PreviewRow(string side, object[] chains)
    {
        public string side { get; } = side;
        public object[] chains { get; } = chains;
    }

    internal sealed class PreviewResult(PreviewRow[] rows, int[][] contactFallbackPairs,
        int[] unlocatedModelIds, int[] missingSupportModelIds, int[] tiltedPartIds, string contactStatus,
        Func<DimensionCompositionPlan> buildCompositionPlan, int unsupportedProposalCount = 0)
        : IEnumerable<PreviewRow>
    {
        /// <summary>Proposals the four-side legacy rows cannot show; they remain in the plan as Blocked.</summary>
        public int UnsupportedProposalCount { get; } = unsupportedProposalCount;
        public PreviewRow[] Rows { get; } = rows;
        public int[][] ContactFallbackPairs { get; } = contactFallbackPairs;
        public int[] UnlocatedModelIds { get; } = unlocatedModelIds;
        public int[] MissingSupportModelIds { get; } = missingSupportModelIds;
        public int[] TiltedPartIds { get; } = tiltedPartIds;
        public string ContactStatus { get; } = contactStatus;
        private readonly Lazy<DimensionCompositionPlan> _compositionPlan = new(buildCompositionPlan);
        public DimensionCompositionPlan CompositionPlan => _compositionPlan.Value;

        // Only detailed diagnostics access the plan. Failures here cannot affect legacy rows.
        public object ProjectCompositionPlan(Func<DimensionCompositionPlan, object>? project = null)
        {
            try { return project == null ? CompositionPlan.Project() : project(CompositionPlan); }
            catch (Exception ex) { return new { error = ex.Message }; }
        }
        public IEnumerator<PreviewRow> GetEnumerator() => ((IEnumerable<PreviewRow>)Rows).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static PreviewResult Build(DimensionPointCatalog catalog, GeometryGroup group,
        IReadOnlyCollection<int> includedIds, double minimumSegment,
        Func<ViewContactCandidatePointsResult?> getContacts, string contextId, int viewId,
        OverallDimensionSettings? overallSettings = null, DimensionCoordinateSettings? coordinateSettings = null,
        IReadOnlyCollection<string>? referenceZones = null, GeometryGroup? referenceGroup = null,
        DimensionPointCatalog? panelCatalog = null)
    {
        if (group.Extent == null)
            return new PreviewResult(Rows(side => [Empty(side, "location", "panel outline is empty")]),
                [], [], [], [], "not-available", () => BuildCompositionPlan(new DimensionRuleEvaluation([]), contextId, viewId, coordinateSettings));

        var panel = group.Extent;
        var input = new TimberPanelPartLocationInput(group, includedIds, getContacts, referenceGroup, panelCatalog);
        var context = DimensionRuleContext.FromCatalog(catalog, panel, input);
        var location = new TimberPanelPartLocationRule(new TimberPanelPartLocationSettings(minimumSegment, referenceZones)).Calculate(context);
        var overall = new OverallDimensionRule(overallSettings ?? new OverallDimensionSettings()).Calculate(
            DimensionRuleContext.FromCatalog(panelCatalog ?? catalog, panel));
        var evaluation = new DimensionRuleEvaluation(location.Results.Concat(overall.Results), location.Diagnostics);
        return FromEvaluation(evaluation, contextId, viewId, coordinateSettings);
    }

    internal static PreviewResult FromEvaluation(DimensionRuleEvaluation evaluation, string contextId, int viewId,
        DimensionCoordinateSettings? coordinateSettings = null)
    {
        var rows = Rows(side => evaluation.Results
            .Where(result => AxisAlignedDimensionRulePreviewAdapter.TryGetSide(result, out var resolvedSide) && resolvedSide == side)
            .Select(result => result.Kind == "location" ? Location(result) : AxisAlignedDimensionRulePreviewAdapter.ToPreview(result))
            .ToArray());
        return new PreviewResult(rows,
            (int[][])evaluation.Diagnostics["contactFallbackPairs"],
            (int[])evaluation.Diagnostics["unlocatedModelIds"],
            (int[])evaluation.Diagnostics["missingSupportModelIds"],
            (int[])evaluation.Diagnostics["tiltedPartIds"],
            (string)evaluation.Diagnostics["contactStatus"], () => BuildCompositionPlan(evaluation, contextId, viewId, coordinateSettings),
            evaluation.Results.Count(result => !AxisAlignedDimensionRulePreviewAdapter.TryGetSide(result, out _)));
    }

    private static DimensionCompositionPlan BuildCompositionPlan(DimensionRuleEvaluation evaluation, string contextId, int viewId,
        DimensionCoordinateSettings? coordinateSettings)
    {
        if (string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("A frozen context ID is required.", nameof(contextId));
        if (viewId <= 0) throw new ArgumentOutOfRangeException(nameof(viewId));
        var proposals = evaluation.Results.Select(result => {
            var hasSide = AxisAlignedDimensionRulePreviewAdapter.TryGetSide(result, out var side);
            var proposal = result;
            if (!hasSide)
            {
                var evidence = result.Evidence.ToDictionary(pair => pair.Key, pair => pair.Value);
                evidence["legacyPreviewRefusal"] = "The panel four-side preview does not support this placement or direction.";
                proposal = new DimensionRuleResult(result.Direction, result.Placement, result.Kind,
                    result.Points, result.Note, result.Segments, evidence, result.Proposal, result.CompositionIntent);
            }
            return proposal.WithProposal(DimensionProposalIdentity.Create(contextId, viewId, "panel",
                hasSide ? side + "-" + result.Kind : null, proposal,
                result.Proposal?.Reference ?? DimensionMeasurementReference.Unknown,
                result.Proposal?.ReferenceSupport ?? DimensionReferenceSupport.Unspecified));
        });
        return DimensionChainComposer.Compose(proposals, evaluation.Diagnostics, coordinateSettings);
    }

    private static PreviewRow[] Rows(Func<DimensionChainSide, object[]> chains) =>
        Enum.GetValues(typeof(DimensionChainSide)).Cast<DimensionChainSide>()
            .Select(side => new PreviewRow(side.ToString(), chains(side))).ToArray();

    private static object Location(DimensionRuleResult result)
    {
        var side = AxisAlignedDimensionRulePreviewAdapter.GetSide(result);
        if (result.Note != null) return Empty(side, result.Kind, result.Note);
        var partIds = (int[][])result.Evidence["pointPartIds"];
        var candidateKinds = (string[][])result.Evidence["candidateKinds"];
        var roles = (string[])result.Evidence["pointRoles"];
        return new {
            side = side.ToString(), kind = result.Kind, pointIds = result.Points.Select(point => point.Id).ToArray(),
            segments = result.Segments,
            points = result.Points.Select((point, index) => new {
                pointId = point.Id, partIds = partIds[index], role = roles[index], candidateKinds = candidateKinds[index]
            }).ToArray(),
            droppedShortPartIds = (int[])result.Evidence["droppedShortPartIds"],
            minimumSegmentViewUnits = (double)result.Evidence["minimumSegmentViewUnits"],
            incomplete = (bool)result.Evidence["incomplete"],
            missingSupportModelIds = (int[])result.Evidence["missingSupportModelIds"],
            incompleteReason = ((int[])result.Evidence["missingSupportModelIds"]).Length > 0
                ? "No selected support for model IDs: " + string.Join(",", (int[])result.Evidence["missingSupportModelIds"])
                : result.Points.Count < 2 ? "fewer than two selected points" : null
        };
    }

    private static object Empty(DimensionChainSide side, string kind, string note) => new {
        side = side.ToString(), kind, pointIds = Array.Empty<string>(), segments = Array.Empty<double>(), points = Array.Empty<object>(), note
    };
}
