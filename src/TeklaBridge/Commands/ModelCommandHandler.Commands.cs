using TeklaMcpServer.Api.Filtering;
using TeklaMcpServer.Api.Selection;

namespace TeklaBridge.Commands;

internal sealed partial class ModelCommandHandler
{
    private const string MissingClassNumberErrorJson = "{\"error\":\"Missing class number\"}";
    private const string InvalidClassNumberErrorJson = "{\"error\":\"Invalid class number\"}";
    private const string MissingObjectTypeOrFilterCriteriaErrorJson = "{\"error\":\"Missing object type or filter criteria\"}";

    private bool HandleGetSelectedProperties()
    {
        var api = new TeklaModelSelectionApi(_model);
        WriteJson(api.GetSelectedObjects());
        return true;
    }

    private bool HandleSelectByClass(string[] args)
    {
        if (args.Length < 2)
        {
            WriteRawJson(MissingClassNumberErrorJson);
            return true;
        }

        if (!int.TryParse(args[1], out var classNumber))
        {
            WriteRawJson(InvalidClassNumberErrorJson);
            return true;
        }

        var api = new TeklaModelSelectionApi(_model);
        var count = api.SelectObjectsByClass(classNumber);
        WriteJson(new { count, @class = classNumber });
        return true;
    }

    private bool HandleGetSelectedWeight()
    {
        var api = new TeklaModelSelectionApi(_model);
        var result = api.GetSelectedObjectsWeight();
        WriteJson(new { totalWeight = result.TotalWeightKg, count = result.Count });
        return true;
    }

    private bool HandleFilterModelObjects(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            WriteRawJson(MissingObjectTypeOrFilterCriteriaErrorJson);
            return true;
        }

        var selectMatches = true;
        if (args.Length >= 3 && bool.TryParse(args[2], out var parsed))
        {
            selectMatches = parsed;
        }

        var api = new TeklaModelFilteringApi(_model);
        var result = api.FilterByType(new ModelObjectFilter
        {
            ObjectType = args[1],
            SelectMatches = selectMatches
        });

        WriteJson(result);
        return true;
    }
}
