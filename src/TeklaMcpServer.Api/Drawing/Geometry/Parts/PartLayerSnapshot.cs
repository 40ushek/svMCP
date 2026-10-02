using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Diagnostic classification over captured properties only, independent of proposal selection.</summary>
internal sealed class PartLayerSnapshot
{
    private readonly IReadOnlyList<PartLayerRule> _rules;
    private readonly IReadOnlyDictionary<int, (PartRoleInView Part, PartLayerResult Layer)> _parts;

    public PartLayerSnapshot(IEnumerable<PartRoleInView> parts, IReadOnlyList<PartLayerRule>? rules)
    {
        _rules = PartRoleClassifier.ValidateLayerRules(rules);
        _parts = parts.ToDictionary(part => part.ModelId,
            part => (part, PartRoleClassifier.ClassifyLayer(part.Name, part.PartPrefix, part.Profile, _rules, part.Material)));
    }

    public object Project() => new {
        policyVersion = PartRoleClassifier.LayerPolicyVersion,
        purpose = "diagnostics only; does not change structural inclusion, selected points or composition decisions",
        rules = _rules.Select(rule => rule.Project()).ToArray(),
        parts = _parts.OrderBy(pair => pair.Key).Select(pair => {
            var part = pair.Value.Part;
            var layer = pair.Value.Layer;
            return new {
                modelId = part.ModelId, partPos = part.PartPos, name = part.Name,
                prefix = part.PartPrefix, profile = part.Profile, material = part.Material,
                structuralStatus = part.Role.IsClassified ? part.Role.Role.ToString() : "Unknown",
                structuralRuleId = part.Role.RuleId, structuralReason = part.Role.Reason,
                classStatus = layer.Status.ToString(), className = layer.ClassName, reason = layer.Reason,
                matchedRuleIds = layer.MatchedRuleIds, selectedRuleIds = layer.SelectedRuleIds,
                unresolvedRuleIds = layer.UnresolvedRuleIds
            };
        }).ToArray()
    };

    public object BindPoints(IEnumerable<DimensionRulePoint> points) => points.GroupBy(point => point.Id, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => {
            var sources = group.SelectMany(point => point.Sources).ToArray();
            var owners = sources.Where(source => source.ObjectKind == "part" && source.ModelId.HasValue)
                .Select(source => source.ModelId!.Value).Distinct().OrderBy(id => id).ToArray();
            return new {
                pointId = group.Key, partModelIds = owners,
                missingPartModelIds = owners.Where(id => !_parts.ContainsKey(id)).ToArray(),
                unidentifiedPartSourceCount = sources.Count(source => source.ObjectKind == "part" && !source.ModelId.HasValue),
                nonPartSourceKinds = sources.Where(source => source.ObjectKind != "part").Select(source => source.ObjectKind)
                    .Distinct(StringComparer.Ordinal).OrderBy(kind => kind, StringComparer.Ordinal).ToArray()
            };
        }).ToArray();
}
