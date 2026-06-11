using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using Tekla.Structures.Geometry3d;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    public CombineDimensionsResult CombineDimensions(int? viewId, IReadOnlyList<int>? dimensionIds, bool previewOnly)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var normalizedDimensionIds = dimensionIds?
            .Where(static id => id > 0)
            .Distinct()
            .ToList();
        var debug = GetDimensionGroupReductionDebug(viewId);
        var candidates = DimensionCombineActionPlanner.BuildCandidates(debug, normalizedDimensionIds);
        var result = new CombineDimensionsResult
        {
            PreviewOnly = previewOnly,
            CandidateCount = candidates.Count
        };

        foreach (var candidate in candidates)
        {
            if (!candidate.CanCombine)
            {
                result.Skipped.Add(CreateCombineCandidateResult(candidate, previewOnly, combined: false, createdDimensionId: null, reasonOverride: candidate.Reason));
                continue;
            }

            if (previewOnly)
            {
                result.Combined.Add(CreateCombineCandidateResult(candidate, previewOnly: true, combined: false, createdDimensionId: null));
                continue;
            }

            var applyResult = TryApplyCombineCandidate(activeDrawing, candidate);
            if (!applyResult.Success)
            {
                result.Skipped.Add(CreateCombineCandidateResult(
                    candidate,
                    previewOnly: false,
                    combined: false,
                    createdDimensionId: null,
                    reasonOverride: applyResult.Reason,
                    rollbackAttempted: applyResult.RollbackAttempted,
                    rollbackSucceeded: applyResult.RollbackSucceeded,
                    rollbackReason: applyResult.RollbackReason));
                continue;
            }

            var handoffResult = ResolveArrangeHandoffResult(activeDrawing, candidate, applyResult.CreatedDimensionId);

            result.Combined.Add(CreateCombineCandidateResult(
                candidate,
                previewOnly: false,
                combined: true,
                createdDimensionId: applyResult.CreatedDimensionId,
                deletedDimensionIdsOverride: candidate.DimensionIds,
                arrangeHandoffAttempted: handoffResult.Attempted,
                arrangeHandoffSucceeded: handoffResult.Succeeded,
                arrangeHandoffReason: handoffResult.Reason,
                arrangeHandoffAppliedDimensionIds: handoffResult.AppliedDimensionIds));
        }

        result.CombinedCount = result.Combined.Count(static item => item.Combined);
        result.SkippedCount = result.Skipped.Count;
        return result;
    }

    private static CombineDimensionCandidateResult CreateCombineCandidateResult(
        DimensionCombineActionCandidate candidate,
        bool previewOnly,
        bool combined,
        int? createdDimensionId,
        IReadOnlyList<int>? deletedDimensionIdsOverride = null,
        string? reasonOverride = null,
        bool rollbackAttempted = false,
        bool rollbackSucceeded = false,
        string? rollbackReason = null,
        bool arrangeHandoffAttempted = false,
        bool arrangeHandoffSucceeded = false,
        string? arrangeHandoffReason = null,
        IReadOnlyList<int>? arrangeHandoffAppliedDimensionIds = null)
    {
        var result = new CombineDimensionCandidateResult
        {
            ViewId = candidate.ViewId,
            ViewType = candidate.ViewType,
            DimensionType = candidate.DimensionType,
            PacketIndex = candidate.PacketIndex,
            BaseDimensionId = candidate.BaseDimensionId,
            ConnectivityMode = candidate.ConnectivityMode,
            PreviewOnly = previewOnly,
            Combined = combined,
            CreatedDimensionId = createdDimensionId,
            RollbackAttempted = rollbackAttempted,
            RollbackSucceeded = rollbackSucceeded,
            RollbackReason = rollbackReason ?? string.Empty,
            ArrangeHandoffAttempted = arrangeHandoffAttempted,
            ArrangeHandoffSucceeded = arrangeHandoffSucceeded,
            ArrangeHandoffReason = arrangeHandoffReason ?? string.Empty,
            Distance = candidate.Preview?.Distance ?? 0,
            Reason = reasonOverride ?? candidate.Reason
        };

        result.DimensionIds.AddRange(candidate.DimensionIds);
        result.BlockingReasons.AddRange(candidate.BlockingReasons);
        if (deletedDimensionIdsOverride != null)
            result.DeletedDimensionIds.AddRange(deletedDimensionIdsOverride);
        if (arrangeHandoffAppliedDimensionIds != null)
            result.ArrangeHandoffAppliedDimensionIds.AddRange(arrangeHandoffAppliedDimensionIds);

        if (candidate.Preview != null)
        {
            foreach (var point in candidate.Preview.PointList.OrderBy(static point => point.Order))
            {
                result.PointList.Add(new DrawingPointInfo
                {
                    X = point.X,
                    Y = point.Y,
                    Order = point.Order
                });
            }
        }

        return result;
    }

    private DimensionArrangeHandoffResult ResolveArrangeHandoffResult(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        DimensionCombineActionCandidate candidate,
        int? createdDimensionId)
    {
        if (!createdDimensionId.HasValue)
        {
            return new DimensionArrangeHandoffResult
            {
                Reason = "created_dimension_not_available"
            };
        }

        if (!candidate.ViewId.HasValue)
        {
            return new DimensionArrangeHandoffResult
            {
                Reason = "view_id_unavailable"
            };
        }

        return DimensionCombineArrangeHandoffExecutor.Execute(
            previewOnly: false,
            applyHandoff: () => TryApplyLocalArrangeHandoff(
                activeDrawing,
                candidate.ViewId.Value,
                createdDimensionId.Value));
    }

    private DimensionCombineApplyResult TryApplyCombineCandidate(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        DimensionCombineActionCandidate candidate)
    {
        if (candidate.Preview == null)
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "combine_preview_unavailable"
            };
        }

        var sourceDimensions = FindDimensionSetsById(activeDrawing, candidate.DimensionIds);
        if (sourceDimensions.Count != candidate.DimensionIds.Count)
        {
            var missing = candidate.DimensionIds.Where(id => !sourceDimensions.ContainsKey(id)).OrderBy(static id => id);
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = $"source_dimensions_not_found:{string.Join(",", missing)}"
            };
        }

        if (!sourceDimensions.TryGetValue(candidate.BaseDimensionId, out var baseDimensionSet))
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "base_dimension_not_found"
            };
        }

        if (baseDimensionSet.GetView() is not Tekla.Structures.Drawing.View view)
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "base_view_not_found"
            };
        }

        if (!TryResolveCombineOffsetVector(baseDimensionSet, out var offsetVector))
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "combine_offset_vector_unavailable"
            };
        }

        var attributes = TryGetCombineAttributes(baseDimensionSet);
        var pointList = CreateCombinePointList(candidate.Preview);
        if (pointList.Count < 2)
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "combine_preview_has_too_few_points"
            };
        }

        StraightDimensionSet? created = null;
        var orderedSourceDimensions = candidate.DimensionIds
            .Where(sourceDimensions.ContainsKey)
            .Select(dimensionId => sourceDimensions[dimensionId])
            .ToList();

        return DimensionCombineApplyExecutor.Execute(
            createDimension: () =>
            {
                created = new StraightDimensionSetHandler().CreateDimensionSet(
                    view,
                    pointList,
                    offsetVector,
                    candidate.Preview.Distance,
                    attributes);

                return created?.GetIdentifier().ID;
            },
            deleteSourceDimensions: orderedSourceDimensions
                .Select(static dimensionSet => (System.Action)(() => dimensionSet.Delete()))
                .ToList(),
            commitCombine: () => activeDrawing.CommitChanges("(MCP) CombineDimensions"),
            rollbackDeleteCreatedDimension: () =>
            {
                if (created == null)
                    throw new System.InvalidOperationException("rollback_created_dimension_missing");

                created.Delete();
            },
            commitRollback: () => activeDrawing.CommitChanges("(MCP) RollbackCombineDimensions"));
    }

    private static Dictionary<int, StraightDimensionSet> FindDimensionSetsById(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        IReadOnlyCollection<int> dimensionIds)
    {
        var result = new Dictionary<int, StraightDimensionSet>();
        if (dimensionIds.Count == 0)
            return result;

        var idSet = new HashSet<int>(dimensionIds);
        var allDims = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
        while (allDims.MoveNext())
        {
            if (allDims.Current is not StraightDimensionSet ds)
                continue;

            var id = ds.GetIdentifier().ID;
            if (!idSet.Contains(id))
                continue;

            result[id] = ds;
        }

        return result;
    }

    private static PointList CreateCombinePointList(DimensionCombinePreviewDebugInfo preview)
    {
        var pointList = new PointList();
        foreach (var point in preview.PointList.OrderBy(static point => point.Order))
            pointList.Add(new Point(point.X, point.Y, 0.0));

        return pointList;
    }

    private static StraightDimensionSet.StraightDimensionSetAttributes TryGetCombineAttributes(StraightDimensionSet baseDimensionSet)
    {
        try
        {
            if (baseDimensionSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes attributes)
                return attributes;
        }
        catch
        {
        }

        return DimensionCreatePlacementHelper.CreateAttributes(null);
    }

    private bool TryResolveCombineOffsetVector(StraightDimensionSet baseDimensionSet, out Vector vector)
    {
        vector = default!;

        try
        {
            var snapshot = BuildDimensionSnapshot(baseDimensionSet);
            if (TryNormalizeDirection(snapshot.DirectionX, snapshot.DirectionY, out var direction) &&
                snapshot.TopDirection != 0)
            {
                var upX = -direction.Y * snapshot.TopDirection;
                var upY = direction.X * snapshot.TopDirection;
                vector = new Vector(upX, upY, 0.0);
                return true;
            }
        }
        catch
        {
        }

        var firstSegment = EnumerateSegments(baseDimensionSet).FirstOrDefault();
        if (firstSegment == null)
            return false;

        if (!TryGetUpDirection(firstSegment, out var upDirection))
            return false;

        vector = new Vector(upDirection.X, upDirection.Y, 0.0);
        return true;
    }
}
