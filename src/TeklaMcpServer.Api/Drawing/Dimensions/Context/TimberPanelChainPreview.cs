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
        int[] unlocatedModelIds, int[] missingSupportModelIds, int[] tiltedPartIds, string contactStatus)
        : IEnumerable<PreviewRow>
    {
        public PreviewRow[] Rows { get; } = rows;
        public int[][] ContactFallbackPairs { get; } = contactFallbackPairs;
        public int[] UnlocatedModelIds { get; } = unlocatedModelIds;
        public int[] MissingSupportModelIds { get; } = missingSupportModelIds;
        public int[] TiltedPartIds { get; } = tiltedPartIds;
        public string ContactStatus { get; } = contactStatus;
        public IEnumerator<PreviewRow> GetEnumerator() => ((IEnumerable<PreviewRow>)Rows).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static PreviewResult Build(DimensionPointCatalog catalog, GeometryGroup group,
        IReadOnlyCollection<int> includedIds, double minimumSegment,
        Func<ViewContactCandidatePointsResult?> getContacts, OverallDimensionSettings? overallSettings = null)
    {
        if (group.Extent == null)
            return new PreviewResult(Rows(side => [Empty(side, "location", "panel outline is empty")]),
                [], [], [], [], "not-available");

        var panel = group.Extent;
        var input = new TimberPanelPartLocationInput(group, includedIds, getContacts);
        var context = DimensionRuleContext.FromCatalog(catalog, panel, input);
        var evaluation = new DimensionRuleSet(
            new TimberPanelPartLocationRule(new TimberPanelPartLocationSettings(minimumSegment)),
            new OverallDimensionRule(overallSettings ?? new OverallDimensionSettings())).Calculate(context);
        var rows = Rows(side => evaluation.Results
            .Where(result => AxisAlignedDimensionRulePreviewAdapter.GetSide(result) == side)
            .Select(result => result.Kind == "location" ? Location(result) : AxisAlignedDimensionRulePreviewAdapter.ToPreview(result))
            .ToArray());
        return new PreviewResult(rows,
            (int[][])evaluation.Diagnostics["contactFallbackPairs"],
            (int[])evaluation.Diagnostics["unlocatedModelIds"],
            (int[])evaluation.Diagnostics["missingSupportModelIds"],
            (int[])evaluation.Diagnostics["tiltedPartIds"],
            (string)evaluation.Diagnostics["contactStatus"]);
    }

    private static PreviewRow[] Rows(Func<DimensionChainSide, object[]> chains) =>
        Enum.GetValues(typeof(DimensionChainSide)).Cast<DimensionChainSide>()
            .Select(side => new PreviewRow(side.ToString(), chains(side))).ToArray();

    private static object Location(DimensionRuleResult result)
    {
        var side = AxisAlignedDimensionRulePreviewAdapter.GetSide(result);
        if (result.Note != null) return Empty(side, result.Kind, result.Note);
        var partIds = (int[][])result.Evidence["pointPartIds"];
        var roles = (string[])result.Evidence["pointRoles"];
        return new {
            side = side.ToString(), kind = result.Kind, row = AxisAlignedDimensionRulePreviewAdapter.GetRow(result), pointIds = result.Points.Select(point => point.Id).ToArray(),
            segments = result.Segments,
            points = result.Points.Select((point, index) => new { pointId = point.Id, partIds = partIds[index], role = roles[index] }).ToArray(),
            droppedShortPartIds = (int[])result.Evidence["droppedShortPartIds"],
            minimumSegmentViewUnits = (double)result.Evidence["minimumSegmentViewUnits"],
            incomplete = (bool)result.Evidence["incomplete"]
        };
    }

    private static object Empty(DimensionChainSide side, string kind, string note) => new {
        side = side.ToString(), kind, pointIds = Array.Empty<string>(), segments = Array.Empty<double>(), points = Array.Empty<object>(), note
    };
}
