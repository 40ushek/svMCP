using System.Text.Json;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private void WriteError(string message)
    {
        WriteJson(new { error = message });
    }

    private void WriteJson<T>(T payload)
    {
        _output.WriteLine(JsonSerializer.Serialize(payload));
    }

    private void WriteRawJson(string json)
    {
        _output.WriteLine(json);
    }
}
