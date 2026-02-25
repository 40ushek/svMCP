using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("Check connection to running Tekla Structures instance")]
    public static string CheckConnection()
    {
        var json = RunBridge("check_connection");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out _))
                return $"Not connected. Bridge response:\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
            var model = doc.RootElement.GetProperty("modelName").GetString();
            var path = doc.RootElement.GetProperty("modelPath").GetString();
            return $"Connected. Model: {model}, Path: {path}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }
}
