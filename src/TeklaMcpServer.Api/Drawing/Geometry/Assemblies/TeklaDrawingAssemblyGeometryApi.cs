using System.Collections;
using Tekla.Structures;
using Tekla.Structures.Model;
using ModelAssembly = Tekla.Structures.Model.Assembly;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingAssemblyGeometryApi : IDrawingAssemblyGeometryApi
{
    private readonly Model _model;
    private readonly IDrawingPartSolidGeometryApi _partSolidGeometryApi;
    private readonly IDrawingBoltGeometryApi _boltGeometryApi;

    public TeklaDrawingAssemblyGeometryApi(Model model)
        : this(model, new TeklaDrawingPartSolidGeometryApi(model), new TeklaDrawingBoltGeometryApi(model))
    {
    }

    internal TeklaDrawingAssemblyGeometryApi(
        Model model,
        IDrawingPartSolidGeometryApi partSolidGeometryApi,
        IDrawingBoltGeometryApi boltGeometryApi)
    {
        _model = model;
        _partSolidGeometryApi = partSolidGeometryApi;
        _boltGeometryApi = boltGeometryApi;
    }

    public AssemblyGeometryInViewResult GetAssemblyGeometryInView(int viewId, int modelId)
    {
        var modelObject = _model.SelectModelObject(new Identifier(modelId));
        if (modelObject == null)
            return Fail(viewId, modelId, $"Model object {modelId} not found.");

        if (modelObject is not ModelAssembly assembly)
            return Fail(viewId, modelId, $"Model object {modelId} is not an assembly.");

        var result = new AssemblyGeometryInViewResult
        {
            Success = true,
            ViewId = viewId,
            ModelId = modelId,
            Assembly = new AssemblyGeometry
            {
                ModelId = modelId,
                AssemblyType = assembly.GetAssemblyType().ToString()
            }
        };

        var memberDescriptors = new List<AssemblyPartDescriptor>();
        var seenPartIds = new HashSet<int>();
        var seenAssemblyIds = new HashSet<int> { assembly.Identifier.ID };
        CollectAssemblyParts(assembly, assembly.Identifier.ID, isRootAssembly: true, memberDescriptors, seenPartIds, seenAssemblyIds, result.Assembly.SubAssemblyIds);

        var rootMainPart = TryGetAssemblyMainPart(assembly);
        result.Assembly.MainPartId = rootMainPart?.Identifier.ID ?? 0;

        foreach (var descriptor in memberDescriptors)
        {
            var partSolidResult = _partSolidGeometryApi.GetPartSolidGeometryInView(viewId, descriptor.Part.Identifier.ID);
            if (!partSolidResult.Success)
            {
                result.Assembly.Warnings.Add($"part:{descriptor.Part.Identifier.ID}:{partSolidResult.Error}");
                continue;
            }

            var partGeometry = new AssemblyPartGeometry
            {
                ModelId = descriptor.Part.Identifier.ID,
                OwningAssemblyId = descriptor.OwningAssemblyId,
                IsMainPart = descriptor.IsMainPart,
                IsDirectMember = descriptor.IsDirectMember,
                Name = descriptor.Part.Name,
                Profile = descriptor.Part.Profile?.ProfileString,
                Material = descriptor.Part.Material?.MaterialString,
                PartPos = GetReportString(descriptor.Part, "PART_POS"),
                AssemblyPos = GetReportString(descriptor.Part, "ASSEMBLY_POS"),
                Solid = partSolidResult.Solid
            };

            result.Assembly.PartMembers.Add(partGeometry);
            ExtendBounds(result.Assembly, partGeometry.Solid.BboxMin);
            ExtendBounds(result.Assembly, partGeometry.Solid.BboxMax);
        }

        var seenBoltGroupIds = new HashSet<int>();
        foreach (var part in result.Assembly.PartMembers)
        {
            var boltResult = _boltGeometryApi.GetPartBoltGeometryInView(viewId, part.ModelId);
            if (!boltResult.Success)
            {
                result.Assembly.Warnings.Add($"bolt-part:{part.ModelId}:{boltResult.Error}");
                continue;
            }

            foreach (var boltGroup in boltResult.BoltGroups)
            {
                if (!seenBoltGroupIds.Add(boltGroup.ModelId))
                    continue;

                result.Assembly.BoltGroups.Add(boltGroup);
                ExtendBounds(result.Assembly, boltGroup.BboxMin);
                ExtendBounds(result.Assembly, boltGroup.BboxMax);

                foreach (var point in boltGroup.Positions)
                    ExtendBounds(result.Assembly, point.Point);
            }
        }

        return result;
    }

    private static void CollectAssemblyParts(
        ModelAssembly assembly,
        int rootAssemblyId,
        bool isRootAssembly,
        List<AssemblyPartDescriptor> parts,
        HashSet<int> seenPartIds,
        HashSet<int> seenAssemblyIds,
        List<int> subAssemblyIds)
    {
        var owningAssemblyId = assembly.Identifier.ID;
        var mainPart = TryGetAssemblyMainPart(assembly);
        if (mainPart != null && seenPartIds.Add(mainPart.Identifier.ID))
        {
            parts.Add(new AssemblyPartDescriptor(
                mainPart,
                owningAssemblyId,
                isMainPart: owningAssemblyId == rootAssemblyId,
                isDirectMember: isRootAssembly));
        }

        if (assembly.GetSecondaries() is ArrayList secondaries)
        {
            foreach (var item in secondaries)
            {
                if (item is not ModelPart part)
                    continue;

                if (!seenPartIds.Add(part.Identifier.ID))
                    continue;

                parts.Add(new AssemblyPartDescriptor(
                    part,
                    owningAssemblyId,
                    isMainPart: false,
                    isDirectMember: isRootAssembly));
            }
        }

        if (assembly.GetSubAssemblies() is not ArrayList subAssemblies)
            return;

        foreach (var item in subAssemblies)
        {
            if (item is not ModelAssembly subAssembly)
                continue;

            var subAssemblyId = subAssembly.Identifier.ID;
            if (subAssemblyId != 0 && !subAssemblyIds.Contains(subAssemblyId))
                subAssemblyIds.Add(subAssemblyId);

            if (!seenAssemblyIds.Add(subAssemblyId))
                continue;

            CollectAssemblyParts(
                subAssembly,
                rootAssemblyId,
                isRootAssembly: false,
                parts,
                seenPartIds,
                seenAssemblyIds,
                subAssemblyIds);
        }
    }

    private static ModelPart? TryGetAssemblyMainPart(ModelAssembly assembly)
    {
        if (assembly.GetMainObject() is ModelPart mainObjectPart)
            return mainObjectPart;

#pragma warning disable CS0618
        return assembly.GetMainPart() as ModelPart;
#pragma warning restore CS0618
    }

    private static void ExtendBounds(AssemblyGeometry geometry, double[] point)
    {
        if (point.Length < 3)
            return;

        if (geometry.BboxMin.Length < 3 || geometry.BboxMax.Length < 3)
        {
            geometry.BboxMin = [point[0], point[1], point[2]];
            geometry.BboxMax = [point[0], point[1], point[2]];
            return;
        }

        geometry.BboxMin[0] = System.Math.Min(geometry.BboxMin[0], point[0]);
        geometry.BboxMin[1] = System.Math.Min(geometry.BboxMin[1], point[1]);
        geometry.BboxMin[2] = System.Math.Min(geometry.BboxMin[2], point[2]);

        geometry.BboxMax[0] = System.Math.Max(geometry.BboxMax[0], point[0]);
        geometry.BboxMax[1] = System.Math.Max(geometry.BboxMax[1], point[1]);
        geometry.BboxMax[2] = System.Math.Max(geometry.BboxMax[2], point[2]);
    }

    private static string GetReportString(ModelObject modelObject, string propertyName)
    {
        var value = string.Empty;
        modelObject.GetReportProperty(propertyName, ref value);
        return value;
    }

    private static AssemblyGeometryInViewResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };

    private sealed class AssemblyPartDescriptor
    {
        public AssemblyPartDescriptor(ModelPart part, int owningAssemblyId, bool isMainPart, bool isDirectMember)
        {
            Part = part;
            OwningAssemblyId = owningAssemblyId;
            IsMainPart = isMainPart;
            IsDirectMember = isDirectMember;
        }

        public ModelPart Part { get; }
        public int OwningAssemblyId { get; }
        public bool IsMainPart { get; }
        public bool IsDirectMember { get; }
    }
}
