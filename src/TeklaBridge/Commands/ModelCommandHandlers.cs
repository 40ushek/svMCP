using System.Text.Json;
using TeklaMcpServer.Api.Filtering;
using TeklaMcpServer.Api.Selection;
using Tekla.Structures.Model;

namespace TeklaBridge;

internal partial class Program
{
    private static bool TryHandleModelCommand(string command, string[] args, Model model, TextWriter realOut)
    {
        switch (command)
        {
            case "get_selected_properties":
            {
                var api = new TeklaModelSelectionApi(model);
                realOut.WriteLine(JsonSerializer.Serialize(api.GetSelectedObjects()));
                return true;
            }

            case "select_by_class":
            {
                if (args.Length < 2)
                {
                    realOut.WriteLine("{\"error\":\"Missing class number\"}");
                    return true;
                }

                if (!int.TryParse(args[1], out var classNumber))
                {
                    realOut.WriteLine("{\"error\":\"Invalid class number\"}");
                    return true;
                }

                var api = new TeklaModelSelectionApi(model);
                var count = api.SelectObjectsByClass(classNumber);
                realOut.WriteLine(JsonSerializer.Serialize(new { count, @class = classNumber }));
                return true;
            }

            case "get_selected_weight":
            {
                var api = new TeklaModelSelectionApi(model);
                var result = api.GetSelectedObjectsWeight();
                realOut.WriteLine(JsonSerializer.Serialize(new { totalWeight = result.TotalWeightKg, count = result.Count }));
                return true;
            }

            case "filter_model_objects":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    realOut.WriteLine("{\"error\":\"Missing object type or filter criteria\"}");
                    return true;
                }

                var selectMatches = true;
                if (args.Length >= 3 && bool.TryParse(args[2], out var parsed))
                    selectMatches = parsed;

                var api = new TeklaModelFilteringApi(model);
                var result = api.FilterByType(new ModelObjectFilter
                {
                    ObjectType = args[1],
                    SelectMatches = selectMatches
                });

                realOut.WriteLine(JsonSerializer.Serialize(result));
                return true;
            }

            default:
                return false;
        }
    }
}
