using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A steel, section or panel chain as its rule adapter built it: the fields every chain has are
/// typed, the family's own lists come in <see cref="Details"/> in their output order. It is read
/// directly by the preview; it serializes in the shape the adapters always answered with.
/// </summary>
[JsonConverter(typeof(StructuralChainConverter))]
internal sealed class StructuralChain(string kind, string[] pointIds, double[] segments)
{
    public string Kind { get; } = kind;
    public string[] PointIds { get; } = pointIds;
    public double[] Segments { get; } = segments;
    /// <summary>The side name a steel or panel chain reports; null when the family names none.</summary>
    public string? Side { get; internal set; }
    /// <summary>Per-point roles and parts for a detailed answer; null when the family reports none.</summary>
    public object[]? Points { get; internal set; }
    public (string Name, object? Value)[] Details { get; internal set; } = [];
    public string? Note { get; internal set; }
    /// <summary>The family names a note field, so it is written even when empty.</summary>
    public bool ReportsNote { get; internal set; }

    internal IEnumerable<(string Name, object? Value)> Fields()
    {
        if (Side != null) yield return ("side", Side);
        yield return ("kind", Kind);
        yield return ("pointIds", PointIds);
        yield return ("segments", Segments);
        if (Points != null) yield return ("points", Points);
        foreach (var detail in Details) yield return detail;
        if (ReportsNote || Note != null) yield return ("note", Note);
    }

    /// <summary>A chain refused with a reason instead of built.</summary>
    internal static StructuralChain Empty(string kind, string note, string? side = null,
        params (string Name, object? Value)[] details) => new(kind, [], []) {
        Side = side, Points = [], Details = details, Note = note, ReportsNote = true
    };
}

internal sealed class StructuralChainConverter : JsonConverter<StructuralChain>
{
    public override StructuralChain Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        throw new NotSupportedException("Structural chains are built by their rule adapters");

    public override void Write(Utf8JsonWriter writer, StructuralChain value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, field) in value.Fields())
        {
            if (field == null && options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull) continue;
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, field, field?.GetType() ?? typeof(object), options);
        }
        writer.WriteEndObject();
    }
}
