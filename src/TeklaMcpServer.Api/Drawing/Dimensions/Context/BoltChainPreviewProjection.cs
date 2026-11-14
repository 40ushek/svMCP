using System.Globalization;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>The public Bolt-row shape. Selection and placement never read this representation.</summary>
internal static class BoltChainPreviewProjection
{
    internal static object Project(BoltChainDecision decision, double distance)
    {
        var candidate = decision.Candidate;
        return new {
            key = candidate.Key, direction = string.Join(",", new[] {
                candidate.OutwardNormal.X, candidate.OutwardNormal.Y, 0d }
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
            attributesFile = "standard", kind = BoltChainRule.Kind,
            chainKind = candidate.Kind, boltGroupId = candidate.GroupId,
            partId = candidate.PartId ?? 0, viewClass = candidate.ViewClass.ToString(),
            segments = candidate.Segments, distance,
            points = candidate.Points.Select(point => new[] { point.X, point.Y }).ToArray(),
            sources = candidate.Sources.Project(), holes = candidate.Holes,
            incomplete = !decision.Create, incompleteReason = decision.Reason,
            remark = candidate.Remark is { Length: > 0 } remark ? remark : null
        };
    }

    // Compatibility with the generic dimension-rule diagnostics; the bolt pipeline uses typed decisions.
    internal static DimensionRuleResult RuleResult(BoltChainDecision decision)
    {
        var candidate = decision.Candidate;
        var reason = decision.PolicyRefusal;
        return new(new DimensionDirection(candidate.Axis == 0 ? 1 : 0, candidate.Axis == 0 ? 0 : 1),
            new OutsideOutlineDimensionPlacement(candidate.OutwardNormal, 1), BoltChainRule.Kind, candidate.Points,
            note: reason, segments: candidate.Segments, evidence: new Dictionary<string, object> {
                ["key"] = candidate.Key, ["chainKind"] = candidate.Kind, ["create"] = reason == null,
                ["groupId"] = candidate.GroupId, ["partId"] = candidate.PartId ?? 0,
                ["viewClass"] = candidate.ViewClass.ToString(), ["baseOffset"] = candidate.BaseOffset,
                ["sources"] = candidate.Sources.Project(), ["holes"] = candidate.Holes,
                ["overMainPartChain"] = candidate.OverMainPartChain ?? "",
                ["groupIds"] = candidate.GroupIds.ToArray(),
                ["remark"] = candidate.Remark ?? "",
                ["requiresChain"] = candidate.RequiresChain ?? ""
            });
    }
}
