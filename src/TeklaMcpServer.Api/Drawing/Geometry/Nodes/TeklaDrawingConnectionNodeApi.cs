using System.Linq;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingConnectionNodeApi : IDrawingConnectionNodeApi
{
    private readonly IDrawingAssemblyGeometryApi _assemblyGeometryApi;
    private readonly IDrawingNodeWorkPointApi _nodeWorkPointApi;

    public TeklaDrawingConnectionNodeApi(Model model)
        : this(new TeklaDrawingAssemblyGeometryApi(model), new TeklaDrawingNodeWorkPointApi(model))
    {
    }

    internal TeklaDrawingConnectionNodeApi(
        IDrawingAssemblyGeometryApi assemblyGeometryApi,
        IDrawingNodeWorkPointApi nodeWorkPointApi)
    {
        _assemblyGeometryApi = assemblyGeometryApi;
        _nodeWorkPointApi = nodeWorkPointApi;
    }

    public GetAssemblyConnectionNodesResult GetAssemblyConnectionNodesInView(int viewId, int modelId)
    {
        var assemblyGeometry = _assemblyGeometryApi.GetAssemblyGeometryInView(viewId, modelId);
        if (!assemblyGeometry.Success)
            return Fail(viewId, modelId, assemblyGeometry.Error ?? "Failed to read assembly geometry in view.");

        var workPoints = _nodeWorkPointApi.GetAssemblyWorkPointsInView(viewId, modelId);
        if (!workPoints.Success)
            return Fail(viewId, modelId, workPoints.Error ?? "Failed to read node work points in view.");

        var result = new GetAssemblyConnectionNodesResult
        {
            Success = true,
            ViewId = viewId,
            ModelId = modelId,
            MainPartId = assemblyGeometry.Assembly.MainPartId,
            Warnings = [.. assemblyGeometry.Assembly.Warnings, .. workPoints.Warnings]
        };

        var partsById = assemblyGeometry.Assembly.PartMembers.ToDictionary(p => p.ModelId);
        var boltGroupsById = assemblyGeometry.Assembly.BoltGroups.ToDictionary(b => b.ModelId);

        foreach (var node in workPoints.Nodes)
        {
            var connection = BuildConnectionNode(node, assemblyGeometry.Assembly, partsById, boltGroupsById);
            if (connection.Participants.Count == 0 && connection.NodeKind == DrawingNodeKind.BoltGroup)
            {
                result.Warnings.Add($"connection-node:{node.NodeModelId}:no-participants");
                continue;
            }

            result.Nodes.Add(connection);
        }

        if (result.Nodes.Count == 0)
            return Fail(viewId, modelId, $"Assembly {modelId} does not expose usable connection-aware nodes in view {viewId}.");

        return result;
    }

    private static ConnectionNodeGeometry BuildConnectionNode(
        NodeWorkPointSet node,
        AssemblyGeometry assembly,
        IReadOnlyDictionary<int, AssemblyPartGeometry> partsById,
        IReadOnlyDictionary<int, BoltGroupGeometry> boltGroupsById)
    {
        var result = new ConnectionNodeGeometry
        {
            NodeIndex = node.NodeIndex,
            NodeKind = node.NodeKind,
            SourceModelId = node.NodeModelId,
            MainPartId = node.MainPartId,
            PrimaryWorkPoint = CloneOrEmpty(node.PrimaryPoint),
            SecondaryWorkPoint = CloneOrEmpty(node.SecondaryPoint),
            ReferenceLine = CopyLine(node.ReferenceLine)
        };

        if (node.NodeKind == DrawingNodeKind.BoltGroup && boltGroupsById.TryGetValue(node.NodeModelId, out var boltGroup))
        {
            var participantIds = CollectParticipantIds(boltGroup, partsById);
            var primaryPartId = ChoosePrimaryPartId(boltGroup, assembly.MainPartId, participantIds);
            result.PrimaryPartId = primaryPartId;

            foreach (var participantId in participantIds)
            {
                if (!partsById.TryGetValue(participantId, out var part))
                    continue;

                var role = participantId == primaryPartId
                    ? DrawingConnectionParticipantRole.Primary
                    : participantId == boltGroup.PartToBeBoltedId
                        ? DrawingConnectionParticipantRole.Secondary
                        : DrawingConnectionParticipantRole.Other;

                if (role == DrawingConnectionParticipantRole.Secondary)
                    result.SecondaryPartIds.Add(participantId);

                result.Participants.Add(BuildParticipant(part, role));
            }

            if (result.SecondaryPartIds.Count == 0)
            {
                foreach (var participant in result.Participants)
                {
                    if (participant.PartId != result.PrimaryPartId)
                        result.SecondaryPartIds.Add(participant.PartId);
                }
            }

            return result;
        }

        if (partsById.TryGetValue(assembly.MainPartId, out var mainPart))
        {
            result.PrimaryPartId = mainPart.ModelId;
            result.Participants.Add(BuildParticipant(mainPart, DrawingConnectionParticipantRole.Primary));
        }

        return result;
    }

    private static List<int> CollectParticipantIds(
        BoltGroupGeometry boltGroup,
        IReadOnlyDictionary<int, AssemblyPartGeometry> partsById)
    {
        var result = new List<int>();
        TryAddParticipant(result, partsById, boltGroup.PartToBoltToId);
        TryAddParticipant(result, partsById, boltGroup.PartToBeBoltedId);

        foreach (var partId in boltGroup.OtherPartIds)
            TryAddParticipant(result, partsById, partId);

        return result;
    }

    private static void TryAddParticipant(List<int> participants, IReadOnlyDictionary<int, AssemblyPartGeometry> partsById, int? partId)
    {
        if (!partId.HasValue)
            return;

        if (!partsById.ContainsKey(partId.Value))
            return;

        if (!participants.Contains(partId.Value))
            participants.Add(partId.Value);
    }

    private static int ChoosePrimaryPartId(BoltGroupGeometry boltGroup, int assemblyMainPartId, IReadOnlyList<int> participantIds)
    {
        if (boltGroup.PartToBoltToId.HasValue && participantIds.Contains(boltGroup.PartToBoltToId.Value))
            return boltGroup.PartToBoltToId.Value;

        if (participantIds.Contains(assemblyMainPartId))
            return assemblyMainPartId;

        if (boltGroup.PartToBeBoltedId.HasValue && participantIds.Contains(boltGroup.PartToBeBoltedId.Value))
            return boltGroup.PartToBeBoltedId.Value;

        return participantIds.FirstOrDefault();
    }

    private static ConnectionNodeParticipantInfo BuildParticipant(AssemblyPartGeometry part, DrawingConnectionParticipantRole role)
    {
        return new ConnectionNodeParticipantInfo
        {
            PartId = part.ModelId,
            Role = role,
            IsMainPart = part.IsMainPart,
            Name = part.Name,
            Profile = part.Profile,
            Material = part.Material,
            Center = CreateCenterPoint(part.Solid.BboxMin, part.Solid.BboxMax),
            BboxMin = CloneOrEmpty(part.Solid.BboxMin),
            BboxMax = CloneOrEmpty(part.Solid.BboxMax)
        };
    }

    private static double[] CreateCenterPoint(double[] bboxMin, double[] bboxMax)
    {
        if (bboxMin.Length < 2 || bboxMax.Length < 2)
            return [];

        return
        [
            GetMidpointCoordinate(bboxMin, bboxMax, 0),
            GetMidpointCoordinate(bboxMin, bboxMax, 1),
            GetMidpointCoordinate(bboxMin, bboxMax, 2)
        ];
    }

    private static double GetMidpointCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : firstValue;
        return (firstValue + secondValue) / 2.0;
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return new DrawingLineInfo
        {
            StartX = line.StartX,
            StartY = line.StartY,
            EndX = line.EndX,
            EndY = line.EndY
        };
    }

    private static double[] CloneOrEmpty(double[] point) => point.Length == 0 ? [] : [.. point];

    private static GetAssemblyConnectionNodesResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };
}
