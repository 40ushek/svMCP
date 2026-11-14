using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Model-facing summary only. The saved plan and batch write data remain unchanged.</summary>
internal static class DimensionAnswerProjection
{
    private static readonly HashSet<string> SelectionFields = new(System.StringComparer.Ordinal) {
        "key", "chainKind", "viewClass", "segments", "incomplete", "incompleteReason", "repeat", "remark",
        // A location chain that took over bolt positions, and an overall restored beside it:
        // without them the model cannot tell those bolts are already dimensioned.
        "carries", "restoredBesides"
    };

    internal static JsonElement Compact(JsonElement answer, bool includeDiagnostics = false)
    {
        if (!answer.TryGetProperty("chainPreview", out var rows)) return answer;
        var result = new Dictionary<string, object?>();
        foreach (var property in answer.EnumerateObject())
        {
            if (property.Name == "chainPreview")
                result[property.Name] = rows.EnumerateArray().Select(Row).ToArray();
            else if (includeDiagnostics || property.Name is not "boltSettings" and not "partSpanMatchToleranceMm")
                result[property.Name] = property.Value.Clone();
        }
        // JsonElements retain the existing display precision; this projection never rounds.
        return ViewDimensionContext.Freeze(result);
    }

    private static object Row(JsonElement row) => row.EnumerateObject().ToDictionary(property => property.Name,
        property => property.Name == "chains"
            ? (object)property.Value.EnumerateArray().Select(chain => chain.EnumerateObject()
                .Where(field => SelectionFields.Contains(field.Name) && field.Value.ValueKind != JsonValueKind.Null)
                .ToDictionary(field => field.Name, field => field.Value.Clone())).ToArray()
            : property.Value.Clone());
}
