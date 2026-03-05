using System.IO;
using System.Text.Json;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Filtering;
using TeklaMcpServer.Api.Selection;

namespace TeklaBridge.Commands;

internal sealed class ModelCommandHandler : ICommandHandler
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
            {
                var api = new TeklaModelSelectionApi(_model);
                _output.WriteLine(JsonSerializer.Serialize(api.GetSelectedObjects()));
                return true;
            }

            case "select_by_class":
            {
                if (args.Length < 2)
                {
                    _output.WriteLine("{\"error\":\"Missing class number\"}");
                    return true;
                }

                if (!int.TryParse(args[1], out var classNumber))
                {
                    _output.WriteLine("{\"error\":\"Invalid class number\"}");
                    return true;
                }

                var api = new TeklaModelSelectionApi(_model);
                var count = api.SelectObjectsByClass(classNumber);
                _output.WriteLine(JsonSerializer.Serialize(new { count, @class = classNumber }));
                return true;
            }

            case "get_selected_weight":
            {
                var api = new TeklaModelSelectionApi(_model);
                var result = api.GetSelectedObjectsWeight();
                _output.WriteLine(JsonSerializer.Serialize(new { totalWeight = result.TotalWeightKg, count = result.Count }));
                return true;
            }

            case "filter_model_objects":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing object type or filter criteria\"}");
                    return true;
                }

                var selectMatches = true;
                if (args.Length >= 3 && bool.TryParse(args[2], out var parsed))
                    selectMatches = parsed;

                var api = new TeklaModelFilteringApi(_model);
                var result = api.FilterByType(new ModelObjectFilter
                {
                    ObjectType = args[1],
                    SelectMatches = selectMatches
                });

                _output.WriteLine(JsonSerializer.Serialize(result));
                return true;
            }

            default:
                return false;
        }
    }
}
