using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        Converters = { new CompactDoubleConverter() }
    };

    private static readonly JsonSerializerOptions _observationJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

    private void WriteDimensionObservationJson(DimensionObservationResult payload)
    {
        var partsBytes = JsonSerializer.SerializeToUtf8Bytes(payload.ViewContext.Parts, _observationJsonOptions);
        using var sha256 = SHA256.Create();
        var hash = BitConverter.ToString(sha256.ComputeHash(partsBytes))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        payload.Header.PartsPayloadHash = $"sha256:{hash}";
        payload.Header.PartsPayloadEncoding = "json-utf8";
        _output.WriteLine(JsonSerializer.Serialize(payload, _observationJsonOptions));
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
