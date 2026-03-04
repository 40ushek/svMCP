using System;
using System.Collections;
using Tekla.Structures.Model;
using TsModel = Tekla.Structures.Model.Model;
using TsConnection = Tekla.Structures.Model.Connection;

namespace TeklaMcpServer.Api.Filtering;

public sealed class TeklaModelFilteringApi : IModelFilteringApi
{
    private readonly TsModel _model;

    public TeklaModelFilteringApi(TsModel model)
    {
        _model = model;
    }

    public FilteredModelObjectsResult FilterByType(ModelObjectFilter filter)
    {
        var criteria = (filter.ObjectType ?? string.Empty).Trim();
        var result = new FilteredModelObjectsResult
        {
            ObjectType = criteria,
            SelectionApplied = filter.SelectMatches
        };

        if (string.IsNullOrWhiteSpace(criteria))
            return result;

        var matches = new ArrayList();

        if (LooksLikeFilterCriteria(criteria))
        {
            var filterCollection = FilterHelper.BuildFilterExpressionsWithParentheses(criteria);
            var filteredObjects = _model.GetModelObjectSelector().GetObjectsByFilter(filterCollection);
            foreach (ModelObject modelObject in filteredObjects)
            {
                if (modelObject == null)
                    continue;

                matches.Add(modelObject);
                result.ObjectIds.Add(modelObject.Identifier.ID);
            }
        }
        else
        {
            var allObjects = _model.GetModelObjectSelector().GetAllObjects();
            while (allObjects.MoveNext())
            {
                if (allObjects.Current is not ModelObject modelObject)
                    continue;

                if (!IsTypeMatch(modelObject, criteria))
                    continue;

                matches.Add(modelObject);
                result.ObjectIds.Add(modelObject.Identifier.ID);
            }
        }

        result.Count = result.ObjectIds.Count;

        if (filter.SelectMatches && result.Count > 0)
            new Tekla.Structures.Model.UI.ModelObjectSelector().Select(matches);

        return result;
    }

    private static bool LooksLikeFilterCriteria(string value)
    {
        return value.IndexOf("|", StringComparison.Ordinal) >= 0
            || value.IndexOf(";", StringComparison.Ordinal) >= 0
            || value.IndexOf("(", StringComparison.Ordinal) >= 0
            || value.IndexOf(")", StringComparison.Ordinal) >= 0;
    }

    private static bool IsTypeMatch(ModelObject modelObject, string objectType)
    {
        var normalized = objectType.Trim().ToLowerInvariant();

        return normalized switch
        {
            "bolt" or "bolts" or "boltgroup" or "boltarray" or "boltcircle" or "boltxylist" or "болт" or "болты" => IsBoltLike(modelObject),
            "part" or "parts" => modelObject is Part,
            "beam" or "beams" => modelObject is Beam,
            "plate" or "plates" or "contourplate" => modelObject is ContourPlate,
            "assembly" or "assemblies" => modelObject is Assembly,
            "weld" or "welds" => modelObject is Weld,
            "rebar" or "rebars" or "reinforcement" => modelObject is RebarGroup || modelObject is SingleRebar,
            "connection" or "connections" => modelObject is TsConnection,
            _ => MatchByRuntimeTypeName(modelObject, objectType)
        };
    }

    private static bool IsBoltLike(ModelObject modelObject)
    {
        if (modelObject is BoltGroup)
            return true;

        var typeName = modelObject.GetType().Name;
        return typeName.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchByRuntimeTypeName(ModelObject modelObject, string objectType)
    {
        var typeName = modelObject.GetType().Name;
        return typeName.Equals(objectType, StringComparison.OrdinalIgnoreCase)
            || typeName.IndexOf(objectType, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
