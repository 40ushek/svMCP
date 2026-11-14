using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Immutable published selections. A later query cannot change an earlier plan.</summary>
internal sealed class DimensionPreviewPlans
{
    private const int Capacity = 32;
    private readonly List<Plan> _plans = [];

    internal sealed class Plan(string id, string ruleSet, string chainView, JsonElement rows, string? sheet)
    {
        public string Id { get; } = id;
        public string RuleSet { get; } = ruleSet;
        public string ChainView { get; } = chainView;
        public JsonElement Rows { get; } = rows.Clone();
        public string? Sheet { get; } = sheet;
    }

    internal string Publish(string ruleSet, string chainView, JsonElement rows,
        IReadOnlyList<DimensionChainRepeats.Placed>? sheet)
    {
        var fingerprint = sheet == null ? null : Fingerprint(sheet);
        var existing = _plans.FirstOrDefault(plan => plan.RuleSet == ruleSet && plan.ChainView == chainView
            && plan.Rows.GetRawText() == rows.GetRawText() && plan.Sheet == fingerprint);
        if (existing != null) return existing.Id;
        var plan = new Plan("plan_" + Guid.NewGuid().ToString("N"), ruleSet, chainView, rows, fingerprint);
        if (_plans.Count == Capacity) _plans.RemoveAt(0);
        _plans.Add(plan);
        return plan.Id;
    }

    internal Plan Require(string? id, string ruleSet, string chainView)
    {
        var candidates = _plans.Where(plan => plan.RuleSet == ruleSet && plan.ChainView == chainView
            && (string.IsNullOrWhiteSpace(id) || plan.Id == id)).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException("Unknown, expired or ambiguous preview plan; read chains and pass its previewPlanId");
        return candidates[0];
    }

    internal static string Fingerprint(IReadOnlyList<DimensionChainRepeats.Placed> sheet) =>
        JsonSerializer.Serialize(sheet.OrderBy(dimension => dimension.ViewId).ThenBy(dimension => dimension.DimensionId)
            .Select(dimension => new { dimension.DimensionId, dimension.ViewId, dimension.ViewType,
                dimension.Segments, dimension.Horizontal, dimension.ReadState }));
}
