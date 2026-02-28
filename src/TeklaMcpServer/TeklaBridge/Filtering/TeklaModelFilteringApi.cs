using System;
using System.Collections;
using TeklaMcpServer.Api.Filtering;
using Tekla.Structures.Model;

namespace TeklaBridge.Filtering;

internal sealed class TeklaModelFilteringApi : IModelFilteringApi
{
    private readonly Model _model;

    public TeklaModelFilteringApi(Model model)
    {
        _model = model;
    }

    public FilteredModelObjectsResult FilterByType(ModelObjectFilter filter)
    {
        var result = new FilteredModelObjectsResult
        {
            ObjectType = (filter.ObjectType ?? string.Empty).Trim(),
            SelectionApplied = filter.SelectMatches
        };

        if (string.IsNullOrWhiteSpace(result.ObjectType))
            return result;

        var matches = new ArrayList();
        var allObjects = _model.GetModelObjectSelector().GetAllObjects();
        while (allObjects.MoveNext())
        {
            if (allObjects.Current is not ModelObject modelObject)
                continue;

            if (!IsTypeMatch(modelObject, result.ObjectType))
                continue;

            matches.Add(modelObject);
            result.ObjectIds.Add(modelObject.Identifier.ID);
        }

        result.Count = result.ObjectIds.Count;

        if (filter.SelectMatches && result.Count > 0)
            new Tekla.Structures.Model.UI.ModelObjectSelector().Select(matches);

        return result;
    }

    private static bool IsTypeMatch(ModelObject modelObject, string objectType)
    {
        var normalized = objectType.Trim().ToLowerInvariant();

        return normalized switch
        {
            "bolt" or "bolts" or "boltgroup" => modelObject is BoltGroup,
            "part" or "parts" => modelObject is Part,
            "beam" or "beams" => modelObject is Beam,
            "plate" or "plates" or "contourplate" => modelObject is ContourPlate,
            "assembly" or "assemblies" => modelObject is Assembly,
            "weld" or "welds" => modelObject is Weld,
            "rebar" or "rebars" or "reinforcement" => modelObject is RebarGroup || modelObject is SingleRebar,
            "connection" or "connections" => modelObject is Connection,
            _ => MatchByRuntimeTypeName(modelObject, objectType)
        };
    }

    private static bool MatchByRuntimeTypeName(ModelObject modelObject, string objectType)
    {
        var typeName = modelObject.GetType().Name;
        return typeName.Equals(objectType, StringComparison.OrdinalIgnoreCase)
            || typeName.IndexOf(objectType, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
