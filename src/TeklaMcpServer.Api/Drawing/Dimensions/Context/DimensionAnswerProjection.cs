using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Model-facing summary only. The saved plan and batch write data remain unchanged.
/// Each row splits into <c>chains</c>, the ones the code allows to place, and <c>refused</c>,
/// the ones it does not, as key and reason. The projection only shortens the code's existing
/// verdicts (incomplete, repeat, bolt class, unread bolts); it adds no rule of its own.
/// </summary>
internal static class DimensionAnswerProjection
{
    // A placeable chain: incomplete, its reason and repeat are false or empty by definition.
    // carries / restoredBesides: a location chain that took over bolt positions, and an overall
    // restored beside it; without them the model cannot tell those bolts are dimensioned.
    private static readonly HashSet<string> EligibleFields = new(System.StringComparer.Ordinal) {
        "key", "chainKind", "viewClass", "segments", "remark", "carries", "restoredBesides"
    };

    internal static JsonElement Compact(JsonElement answer, bool includeDiagnostics = false)
    {
        if (!answer.TryGetProperty("chainPreview", out var rows)) return answer;
        var result = new Dictionary<string, object?>();
        foreach (var property in answer.EnumerateObject())
        {
            if (property.Name == "chainPreview")
                result[property.Name] = rows.EnumerateArray().Select(Row).ToArray();
            // The verdicts are on the chains themselves; the repeats list only repeats them.
            else if (property.Name == "repeats") continue;
            else if (includeDiagnostics || property.Name is not "boltSettings" and not "partSpanMatchToleranceMm")
                result[property.Name] = property.Value.Clone();
        }
        // JsonElements retain the existing display precision; this projection never rounds.
        return ViewDimensionContext.Freeze(result);
    }

    private static Dictionary<string, object?> Row(JsonElement row)
    {
        var bolt = row.TryGetProperty("side", out var side) && side.GetString() == "Bolt";
        var unread = bolt && row.TryGetProperty("readIncomplete", out var why)
            && why.ValueKind == JsonValueKind.String && why.GetString() is { Length: > 0 };
        var result = new Dictionary<string, object?>();
        var chains = new List<Dictionary<string, object?>>();
        var refused = new List<Dictionary<string, object?>>();
        foreach (var property in row.EnumerateObject())
        {
            if (property.Name != "chains")
            {
                result[property.Name] = property.Value.Clone();
                continue;
            }
            foreach (var chain in property.Value.EnumerateArray())
            {
                var reason = Refusal(chain, bolt, unread);
                if (reason == null) chains.Add(Fields(chain, EligibleFields));
                else refused.Add(Refused(chain, reason.Value));
            }
        }
        result["chains"] = chains;
        if (refused.Count > 0) result["refused"] = refused;
        return result;
    }

    /// <summary>Why the code does not allow this chain, or null when it does.</summary>
    private static (string Code, string? Note)? Refusal(JsonElement chain, bool bolt, bool unread)
    {
        var note = Text(chain, "incompleteReason");
        if (Text(chain, "repeat") is { } repeat) return ("repeat", note ?? repeat);
        if (chain.TryGetProperty("incomplete", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
            return ("incomplete", note);
        if (!bolt) return null;
        if (unread) return ("boltReadIncomplete", null);
        var viewClass = Text(chain, "viewClass");
        return viewClass == "Plan" ? null : ("boltClass", viewClass ?? "unknown");
    }

    private static Dictionary<string, object?> Refused(JsonElement chain, (string Code, string? Note) reason)
    {
        var entry = new Dictionary<string, object?> { ["key"] = Text(chain, "key"), ["reason"] = reason.Code };
        if (reason.Note != null) entry["note"] = reason.Note;
        return entry;
    }

    private static Dictionary<string, object?> Fields(JsonElement chain, HashSet<string> names) =>
        chain.EnumerateObject().Where(field => names.Contains(field.Name) && field.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(field => field.Name, field => (object?)field.Value.Clone());

    private static string? Text(JsonElement chain, string name) =>
        chain.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text : null;
}
