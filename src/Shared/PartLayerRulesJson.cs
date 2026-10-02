using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Shared;

/// <summary>Shared wire validation, independent of Tekla and domain classification.</summary>
internal static class PartLayerRulesJson
{
    public static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json!);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array) throw new ArgumentException("layerRules must be a JSON array.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in root.EnumerateArray())
            {
                ValidateObject(rule, "rule", "id", "className", "priority", "conditions", "data");
                var id = RequiredString(rule, "id");
                RequiredString(rule, "className");
                if (!ids.Add(id)) throw new ArgumentException("layerRules rule IDs must be unique: " + id);
                if (rule.TryGetProperty("priority", out var priority) &&
                    (priority.ValueKind != JsonValueKind.Number || !priority.TryGetInt32(out _)))
                    throw new ArgumentException("layerRules priority must be a 32-bit integer.");
                if (!rule.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Array || conditions.GetArrayLength() == 0)
                    throw new ArgumentException("layerRules conditions must be a non-empty array.");
                foreach (var condition in conditions.EnumerateArray())
                {
                    ValidateObject(condition, "condition", "property", "matchKind", "value");
                    Vocabulary(RequiredString(condition, "property"), "property", "Name", "Prefix", "Profile", "Material");
                    RequiredString(condition, "value");
                    if (condition.TryGetProperty("matchKind", out _))
                        Vocabulary(RequiredString(condition, "matchKind"), "matchKind", "Equals", "Contains", "StartsWith");
                }
                if (rule.TryGetProperty("data", out var data))
                {
                    if (data.ValueKind != JsonValueKind.Object) throw new ArgumentException("layerRules data must be an object.");
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var entry in data.EnumerateObject())
                        if (string.IsNullOrWhiteSpace(entry.Name) || !keys.Add(entry.Name))
                            throw new ArgumentException("layerRules data requires unique, non-empty keys.");
                }
            }
            return root.Clone();
        }
        catch (JsonException ex) { throw new ArgumentException("Invalid layerRules JSON: " + ex.Message, nameof(json), ex); }
    }

    private static void ValidateObject(JsonElement value, string kind, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("layerRules " + kind + " must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!fields.Contains(field.Name) || !seen.Add(field.Name))
                throw new ArgumentException("layerRules " + kind + " has an unknown or duplicate field: " + field.Name);
    }

    private static string RequiredString(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
            throw new ArgumentException("layerRules " + field + " must be a non-empty string.");
        return text.GetString()!;
    }

    private static void Vocabulary(string value, string field, params string[] supported)
    {
        if (!supported.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("layerRules " + field + " must be one of: " + string.Join(", ", supported));
    }
}
