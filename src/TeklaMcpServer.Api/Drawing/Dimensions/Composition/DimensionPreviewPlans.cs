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

    internal sealed class Plan
    {
        public string Id { get; }
        public string RuleSet { get; }
        public string ChainView { get; }
        public JsonElement Rows { get; }
        public string? Sheet { get; }
        private JsonElement Answer { get; }
        internal string AnswerText => Answer.GetRawText();

        /// <summary>The plan is the published answer itself: batch reads its rows, details return it whole.</summary>
        internal Plan(string id, string ruleSet, string chainView, JsonElement answer, string? sheet)
        {
            Id = id;
            RuleSet = ruleSet;
            ChainView = chainView;
            Sheet = sheet;
            Answer = answer.Clone();
            Rows = Answer.GetProperty("chainPreview");
        }

        internal JsonElement Details()
        {
            var result = Answer.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
            result["previewPlanId"] = Id;
            result["ruleSet"] = RuleSet;
            result["chainView"] = ChainView == "chaindetails" ? "chainDetails" : ChainView;
            return ViewDimensionContext.Freeze(result);
        }
    }

    /// <summary>
    /// Saves an answer that has <c>chainPreview</c>. The same answer on the same sheet gets the
    /// same plan; another answer (other questions, other diagnostics) gets its own plan, so the
    /// details of a plan are always the answer that named it.
    /// </summary>
    internal string Publish(string ruleSet, string chainView, JsonElement answer,
        IReadOnlyList<DimensionChainRepeats.Placed>? sheet)
    {
        var fingerprint = sheet == null ? null : Fingerprint(sheet);
        var text = answer.GetRawText();
        var existing = _plans.FirstOrDefault(plan => plan.RuleSet == ruleSet && plan.ChainView == chainView
            && plan.AnswerText == text && plan.Sheet == fingerprint);
        if (existing != null) return existing.Id;
        var plan = new Plan("plan_" + Guid.NewGuid().ToString("N"), ruleSet, chainView, answer, fingerprint);
        if (_plans.Count == Capacity) _plans.RemoveAt(0);
        _plans.Add(plan);
        return plan.Id;
    }

    internal Plan? Find(string id) => _plans.FirstOrDefault(plan => plan.Id == id);

    /// <summary>The sheet the plan was read on is gone: the same refusal a batch gives.</summary>
    internal static void RequireSameSheet(Plan plan, Func<IReadOnlyList<DimensionChainRepeats.Placed>> readSheet)
    {
        if (plan.Sheet != null && plan.Sheet != Fingerprint(readSheet()))
            throw new InvalidOperationException("Drawing dimensions changed after the preview; read a new plan before writing");
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
