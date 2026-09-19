using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The default, smaller answer of `get_drawing_parts`. It does two things, and only these:
///
/// 1. Leaves out object types that are not parts a dimension is planned from - bolt arrays, edge
///    chamfers, reference models and connections - but counts every hidden object by type, so the
///    omission is visible rather than silent. `includeTypes` names types to bring back.
/// 2. Merges records that are identical in EVERY read field, `partPrefix` and `partPrefixKnown`
///    included, into one entry listing all their model ids. Two parts with the same position and
///    profile but a differently read prefix are never merged: an unread prefix must stay visible
///    as unread.
///
/// The list is the whole drawing's objects, not those of one view; `scope` says so.
/// `total` keeps its old meaning (objects before any filtering); the other counts are explicit.
/// </summary>
public static class CompactDrawingParts
{
    public static readonly IReadOnlyList<string> DefaultHiddenTypes =
        new[] { "BoltArray", "EdgeChamfer", "ReferenceModel", "Connection" };

    public static Dictionary<string, object?> Project(IReadOnlyList<DrawingPartInfo> parts, int total, string? includeTypes)
    {
        if (parts == null) throw new ArgumentNullException(nameof(parts));

        var includeAll = false;
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in (includeTypes ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = name.Trim();
            if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase)) includeAll = true;
            else if (trimmed.Length > 0) include.Add(trimmed);
        }

        var hidden = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var order = new List<(string Type, string PartPos, string PartPrefix, bool PrefixKnown,
            string AssemblyPos, string Profile, string Material, string Name)>();
        var groups = new Dictionary<(string Type, string PartPos, string PartPrefix, bool PrefixKnown,
            string AssemblyPos, string Profile, string Material, string Name), List<int>>();

        foreach (var part in parts)
        {
            var isHiddenType = DefaultHiddenTypes.Contains(part.Type, StringComparer.OrdinalIgnoreCase);
            if (isHiddenType && !includeAll && !include.Contains(part.Type))
            {
                hidden[part.Type] = hidden.TryGetValue(part.Type, out var count) ? count + 1 : 1;
                continue;
            }

            var key = (part.Type, part.PartPos, part.PartPrefix, part.PartPrefixKnown,
                part.AssemblyPos, part.Profile, part.Material, part.Name);
            if (!groups.TryGetValue(key, out var ids))
            {
                order.Add(key);
                groups[key] = ids = new List<int>();
            }

            ids.Add(part.ModelId);
        }

        var entries = order.Select(key => new Dictionary<string, object?>
        {
            ["type"] = key.Type,
            ["partPos"] = key.PartPos,
            ["partPrefix"] = key.PartPrefix,
            ["partPrefixKnown"] = key.PrefixKnown,
            ["assemblyPos"] = key.AssemblyPos,
            ["profile"] = key.Profile,
            ["material"] = key.Material,
            ["name"] = key.Name,
            ["count"] = groups[key].Count,
            ["modelIds"] = groups[key]
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["format"] = "compact",
            ["scope"] = "drawing",
            ["total"] = total,
            ["returnedObjects"] = groups.Values.Sum(ids => ids.Count),
            ["returnedGroups"] = entries.Count,
            ["hiddenObjects"] = hidden.Values.Sum(),
            ["hiddenByType"] = hidden,
            ["parts"] = entries
        };
    }
}
