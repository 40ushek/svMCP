using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Typed structural chains to preview chains, and final response serialization.
/// Composition/repeat selection operates only on typed DimensionPreviewChain fields.</summary>
internal static class DimensionPreviewProjection
{
    internal static JsonElement Freeze(object value, bool display = false)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, display ? DisplayOptions : null));
        return doc.RootElement.Clone();
    }

    private static readonly JsonSerializerOptions DisplayOptions = new() { Converters = { new DisplayDoubleConverter() } };
    private sealed class DisplayDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
            writer.WriteRawValue(Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture));
    }


    internal static string Direction(double x, double y) => string.Join(",", new[] { x, y, 0d }
        .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

    internal static DimensionPreviewChain Structural(StructuralChain source, Dictionary<string, object?> projection)
    {
        var key = projection.TryGetValue("key", out var name) ? name as string ?? "" : "";
        var direction = projection.TryGetValue("direction", out var dir) ? dir as string : null;
        var attributes = projection.TryGetValue("attributesFile", out var file) ? file as string ?? "standard" : "standard";
        var segments = source.Segments.Select(value => (double)Math.Round((decimal)value, 3)).ToArray();
        var incomplete = projection.TryGetValue("incomplete", out var omitted) && omitted is true;
        var reason = projection.TryGetValue("incompleteReason", out var why) ? why as string : null;
        return new DimensionPreviewChain(key, source.Kind, direction, attributes, source.PointIds, segments, incomplete, reason, projection);
    }

    internal static object Output(DimensionPreviewChain chain, JsonSerializerOptions options)
    {
        if (chain.Carries != null)
        {
            var carried = new Dictionary<string, object?> {
                ["key"] = chain.Key, ["direction"] = chain.Direction, ["attributesFile"] = chain.AttributesFile,
                ["kind"] = chain.Kind, ["chainKind"] = chain.ChainKind, ["viewClass"] = "Plan", ["partId"] = chain.PartId,
                ["carries"] = chain.Carries, ["points"] = chain.Points, ["segments"] = chain.Segments,
                ["distance"] = chain.Distance, ["incomplete"] = chain.Incomplete,
                ["sources"] = BoltChainPreviewProjection.Sources(chain.Sources)
            };
            ApplySelection(carried, chain);
            return carried;
        }
        if (!chain.SelectionChanged && !chain.PointIdsChanged)
            return chain.OutputTemplate!;
        // One coordinate policy: calculated numbers leave through the caller's display options
        // (rounded); captured source points are JsonElements and keep their source precision.
        var template = JsonSerializer.SerializeToElement(chain.OutputTemplate, options);
        var result = template.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        if (chain.PointIdsChanged) { result["pointIds"] = chain.PointIds; result["segments"] = chain.Segments; }
        ApplySelection(result, chain);
        if (chain.RestoredBesides != null) result["restoredBesides"] = chain.RestoredBesides;
        return result;
    }

    private static void ApplySelection(Dictionary<string, object?> result, DimensionPreviewChain chain)
    {
        if (!chain.SelectionChanged) return;
        result["incomplete"] = chain.Incomplete;
        if (chain.Repeat != null) result["repeat"] = chain.Repeat;
        if (chain.Reason != null) result["incompleteReason"] = chain.Reason;
        else result.Remove("incompleteReason");
    }
}

internal sealed class DimensionPreviewChainConverter : JsonConverter<DimensionPreviewChain>
{
    public DimensionPreviewChainConverter() { }
    public override DimensionPreviewChain Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        throw new NotSupportedException("Preview chains are constructed from typed calculation results");
    public override void Write(Utf8JsonWriter writer, DimensionPreviewChain value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, DimensionPreviewProjection.Output(value, options), options);
}

internal sealed class DimensionPreviewRowConverter : JsonConverter<DimensionPreviewRow>
{
    public DimensionPreviewRowConverter() { }
    public override DimensionPreviewRow Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        throw new NotSupportedException("Preview rows are constructed from typed calculation results");
    public override void Write(Utf8JsonWriter writer, DimensionPreviewRow value, JsonSerializerOptions options)
    {
        if (value.Side == ViewDimensionContext.BoltSide)
            JsonSerializer.Serialize(writer, new { side = value.Side, readIncomplete = value.ReadIncomplete, chains = value.Chains }, options);
        else JsonSerializer.Serialize(writer, new { side = value.Side, chains = value.Chains }, options);
    }
}
