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

        var previousAutoFetch = ModelObjectEnumerator.AutoFetch;
        ModelObjectEnumerator.AutoFetch = false;
        try
        {
            if (LooksLikeFilterCriteria(criteria))
            {
                var filterCollection = FilterHelper.BuildFilterExpressionsWithParentheses(criteria);
                var filteredObjects = _model.GetModelObjectSelector().GetObjectsByFilter(filterCollection);
                while (filteredObjects.MoveNext())
                {
                    if (filteredObjects.Current is not ModelObject modelObject)
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
        }
        finally
        {
            ModelObjectEnumerator.AutoFetch = previousAutoFetch;
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
        var typeName = modelObject.GetType().Name;

        return normalized switch
        {
            "bolt" or "bolts" or "boltgroup" or "boltarray" or "boltcircle" or "boltxylist" or "болт" or "болты"
                => typeName.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) >= 0,
            "part" or "parts"
                => IsPartLike(modelObject),
            "beam" or "beams"
                => typeName.Equals("Beam", StringComparison.OrdinalIgnoreCase),
            "plate" or "plates" or "contourplate"
                => typeName.Equals("ContourPlate", StringComparison.OrdinalIgnoreCase),
            "assembly" or "assemblies"
                => typeName.Equals("Assembly", StringComparison.OrdinalIgnoreCase),
            "weld" or "welds"
                => typeName.Equals("Weld", StringComparison.OrdinalIgnoreCase),
            "rebar" or "rebars" or "reinforcement"
                => typeName.Equals("RebarGroup", StringComparison.OrdinalIgnoreCase) || typeName.Equals("SingleRebar", StringComparison.OrdinalIgnoreCase),
            "connection" or "connections"
                => typeName.Equals("Connection", StringComparison.OrdinalIgnoreCase),
            _ => typeName.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                 || typeName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
        };
    }

    private static bool IsPartLike(ModelObject modelObject)
    {
        // `is Part` fails on .NET Remoting transparent proxies.
        // GetType() returns the actual remote type, so walk the inheritance chain instead.
        var type = modelObject.GetType();
        while (type != null && type != typeof(object))
        {
            if (type == typeof(Part))
                return true;
            type = type.BaseType;
        }
        return false;
    }
}
