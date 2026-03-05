using System;
using System.Collections;
using System.Collections.Generic;
using Tekla.Structures.Model;
using TsModel = Tekla.Structures.Model.Model;
using TsConnection = Tekla.Structures.Model.Connection;

namespace TeklaMcpServer.Api.Selection;

public sealed class TeklaModelSelectionApi : IModelSelectionApi
{
    private readonly TsModel _model;

    public TeklaModelSelectionApi(TsModel model)
    {
        _model = model;
    }

    public IReadOnlyList<ModelObjectInfo> GetSelectedObjects()
    {
        var objs = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();
        var results = new List<ModelObjectInfo>();

        while (objs.MoveNext())
        {
            // `as ModelObject` fails for .NET Remoting proxies (same as `is`).
            // Explicit cast routes through the proxy and works correctly.
            ModelObject? current = null;
            try { current = (ModelObject)objs.Current; } catch { }
            var info = CreateObjectInfo(current);
            if (info != null)
                results.Add(info);
        }

        return results;
    }

    public int SelectObjectsByClass(int classNumber)
    {
        var className = classNumber.ToString();
        var allObjs = _model.GetModelObjectSelector().GetAllObjects();
        var toSelect = new ArrayList();

        while (allObjs.MoveNext())
        {
            if (allObjs.Current is Part p && p.Class == className)
                toSelect.Add(p);
        }

        if (toSelect.Count > 0)
            new Tekla.Structures.Model.UI.ModelObjectSelector().Select(toSelect);

        return toSelect.Count;
    }

    public SelectedWeightResult GetSelectedObjectsWeight()
    {
        var objs = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();
        double totalWeight = 0;
        int count = 0;

        while (objs.MoveNext())
        {
            if (objs.Current is not ModelObject obj) continue;

            double w = 0;
            if (obj.GetReportProperty("WEIGHT", ref w) && w > 0)
            {
                totalWeight += w;
                count++;
            }
        }

        return new SelectedWeightResult
        {
            Count = count,
            TotalWeightKg = Math.Round(totalWeight, 2)
        };
    }

    private static ModelObjectInfo? CreateObjectInfo(ModelObject? obj)
    {
        if (obj == null) return null;

        if (obj is Part part)
        {
            double weight = 0;
            part.GetReportProperty("WEIGHT", ref weight);

            return new ModelObjectInfo
            {
                Id = part.Identifier.ID,
                Guid = part.Identifier.GUID.ToString(),
                Type = part.GetType().Name,
                Name = part.Name,
                Profile = part.Profile.ProfileString,
                Material = part.Material.MaterialString,
                Class = part.Class,
                WeightKg = Math.Round(weight, 3)
            };
        }

        if (obj is BoltGroup bolt)
        {
            return new ModelObjectInfo
            {
                Id = bolt.Identifier.ID,
                Guid = bolt.Identifier.GUID.ToString(),
                Type = bolt.GetType().Name,
                BoltStandard = bolt.BoltStandard,
                BoltSize = bolt.BoltSize
            };
        }

        var typeName = obj.GetType().Name;
        if (typeName == "Component" || typeName == "Connection" || typeName == "Detail")
        {
            // `is Component` fails on .NET Remoting proxies — explicit cast works.
            try
            {
                var component = (Tekla.Structures.Model.Component)obj;
                return new ModelObjectInfo
                {
                    Id = obj.Identifier.ID,
                    Guid = obj.Identifier.GUID.ToString(),
                    Type = typeName,
                    Name = string.IsNullOrWhiteSpace(component.Name) ? null : component.Name,
                    Class = component.Number != 0 ? component.Number.ToString() : null
                };
            }
            catch
            {
                return new ModelObjectInfo
                {
                    Id = obj.Identifier.ID,
                    Guid = obj.Identifier.GUID.ToString(),
                    Type = typeName
                };
            }
        }

        if (obj is Weld weld)
        {
            return new ModelObjectInfo
            {
                Id = weld.Identifier.ID,
                Guid = weld.Identifier.GUID.ToString(),
                Type = "Weld"
            };
        }

        if (obj is RebarGroup rebar)
        {
            return new ModelObjectInfo
            {
                Id = rebar.Identifier.ID,
                Guid = rebar.Identifier.GUID.ToString(),
                Type = rebar.GetType().Name,
                Name = rebar.Name
            };
        }

        if (obj is SingleRebar singleRebar)
        {
            return new ModelObjectInfo
            {
                Id = singleRebar.Identifier.ID,
                Guid = singleRebar.Identifier.GUID.ToString(),
                Type = "SingleRebar",
                Name = singleRebar.Name
            };
        }

        // Generic fallback for any other type
        return new ModelObjectInfo
        {
            Id = obj.Identifier.ID,
            Guid = obj.Identifier.GUID.ToString(),
            Type = obj.GetType().Name
        };
    }
}
