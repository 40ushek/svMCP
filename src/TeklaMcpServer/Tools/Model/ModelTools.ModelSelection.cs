using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("Get properties of selected elements in Tekla model (GUID, name, profile, material, class, weight)")]
    public static string GetSelectedElementsProperties()
    {
        var json = RunBridge("get_selected_properties");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No parts selected. Please select elements in Tekla first.";
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Select elements in Tekla model by class number (Tekla class field)")]
    public static string SelectElementsByClass(
        [Description("Tekla class number, e.g. 1, 2, 99")] int classNumber)
    {
        var json = RunBridge("select_by_class", classNumber.ToString());
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";
            var count = doc.RootElement.GetProperty("count").GetInt32();
            return $"Selected {count} elements with class {classNumber}.";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Get total weight of selected elements in kg")]
    public static string GetSelectedElementsTotalWeight()
    {
        var json = RunBridge("get_selected_weight");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";
            var totalWeight = doc.RootElement.GetProperty("totalWeight").GetDouble();
            var count = doc.RootElement.GetProperty("count").GetInt32();
            if (count == 0) return "No parts selected.";
            return $"Total weight of {count} selected parts: {totalWeight} kg";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }
}
