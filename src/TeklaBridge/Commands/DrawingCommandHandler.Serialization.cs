using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        Converters = { new CompactDoubleConverter() }
    };

    private void WriteError(string message)
    {
        WriteJson(new { error = message });
    }

    private void WriteJson<T>(T payload)
    {
        _output.WriteLine(JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private void WriteRawJson(string json)
    {
        _output.WriteLine(json);
    }

    private sealed class CompactDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
            => writer.WriteRawValue(
                Math.Round(value, 5).ToString("G10", System.Globalization.CultureInfo.InvariantCulture));
    }
}
