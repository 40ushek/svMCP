using System.Collections.Generic;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

public static class DrawingPropertyFilterParser
{
    public static List<DrawingPropertyFilter> Parse(string filtersJson)
    {
        var result = new List<DrawingPropertyFilter>();
        if (string.IsNullOrWhiteSpace(filtersJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(filtersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var property = item.TryGetProperty("property", out var p)
                    ? (p.GetString() ?? string.Empty)
                    : string.Empty;
                var value = item.TryGetProperty("value", out var v)
                    ? (v.GetString() ?? string.Empty)
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(property))
                    result.Add(new DrawingPropertyFilter { Property = property, Value = value });
            }
        }
        catch
        {
        }

        return result;
    }
}
