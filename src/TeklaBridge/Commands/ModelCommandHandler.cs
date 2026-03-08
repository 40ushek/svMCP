using System.IO;
using System.Text.Json;
using Tekla.Structures.Model;

namespace TeklaBridge.Commands;

internal sealed partial class ModelCommandHandler : ICommandHandler
{
    private readonly Model _model;
    private readonly TextWriter _output;

    public ModelCommandHandler(Model model, TextWriter output)
    {
        _model = model;
        _output = output;
    }

    public bool TryHandle(string command, string[] args)
    {
        switch (command)
        {
            case "get_selected_properties":
                return HandleGetSelectedProperties();

            case "select_by_class":
                return HandleSelectByClass(args);

            case "get_selected_weight":
                return HandleGetSelectedWeight();

            case "filter_model_objects":
                return HandleFilterModelObjects(args);

            default:
                return false;
        }
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
