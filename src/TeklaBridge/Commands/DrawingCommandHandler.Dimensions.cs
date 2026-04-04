using System.Linq;
using System.Reflection;
using TeklaMcpServer.Api.Drawing;
using System.Globalization;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleDimensionCommands(string command, string[] args)
    {
        var api = new TeklaDrawingDimensionsApi();

        switch (command)
        {
            case "get_drawing_dimensions":
                return HandleGetDrawingDimensions(api, args);

            case "draw_dimension_text_boxes":
                return HandleDrawDimensionTextBoxes(api, args);

            case "get_dimension_text_placement_debug":
                return HandleGetDimensionTextPlacementDebug(api, args);

            case "get_dimension_source_debug":
                return HandleGetDimensionSourceDebug(api, args);

            case "get_dimension_groups_debug":
                return HandleGetDimensionGroupsDebug(api, args);

            case "get_dimension_arrangement_debug":
                return HandleGetDimensionArrangementDebug(api, args);

            case "arrange_dimensions":
                return HandleArrangeDimensions(api, args);

            case "combine_dimensions":
                return HandleCombineDimensions(api, args);

            case "move_dimension":
                return HandleMoveDimension(api, args);

            case "create_dimension":
                return HandleCreateDimension(api, args);

            case "delete_dimension":
                return HandleDeleteDimension(api, args);

            case "place_control_diagonals":
                return HandlePlaceControlDiagonals(api, args);

            default:
                return false;
        }
    }

    private bool HandleGetDrawingDimensions(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var result = api.GetDimensions(viewId);
        WriteGetDimensionsResult(result);
        return true;
    }

    private bool HandleDrawDimensionTextBoxes(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        int? dimensionId = null;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDimensionId))
            {
                WriteError("dimensionId must be an integer.");
                return true;
            }

            dimensionId = parsedDimensionId;
        }

        var color = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : "Yellow";
        var group = args.Length > 4 && !string.IsNullOrWhiteSpace(args[4]) ? args[4] : "dimension-text-boxes";
        var result = api.DrawDimensionTextBoxes(viewId, dimensionId, color, group);
        WriteJson(new
        {
            group = result.Group,
            clearedCount = result.ClearedCount,
            createdCount = result.CreatedCount,
            createdIds = result.CreatedIds,
            dimensionCount = result.DimensionCount,
            segmentCount = result.SegmentCount
        });
        return true;
    }

    private bool HandleGetDimensionTextPlacementDebug(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        int? dimensionId = null;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDimensionId))
            {
                WriteError("dimensionId must be an integer.");
                return true;
            }

            dimensionId = parsedDimensionId;
        }

        var result = api.GetDimensionTextPlacementDebug(viewId, dimensionId);
        WriteJson(new
        {
            viewId = result.ViewId,
            total = result.Total,
            dimensions = result.Dimensions.Select(d => new
            {
                dimensionId = d.DimensionId,
                dimensionType = d.DimensionType,
                textPlacing = d.TextPlacing,
                shortDimension = d.ShortDimension,
                placingDirectionSign = d.PlacingDirectionSign,
                leftTagLineOffset = d.LeftTagLineOffset,
                rightTagLineOffset = d.RightTagLineOffset,
                segments = d.Segments.Select(s => new
                {
                    segmentId = s.SegmentId,
                    expectedText = s.ExpectedText,
                    dimensionLine = s.DimensionLine == null ? null : new
                    {
                        startX = s.DimensionLine.StartX,
                        startY = s.DimensionLine.StartY,
                        endX = s.DimensionLine.EndX,
                        endY = s.DimensionLine.EndY
                    },
                    selectedSource = s.SelectedSource,
                    relatedTextCandidates = s.RelatedTextCandidates.Select(c => new
                    {
                        owner = c.Owner,
                        type = c.Type,
                        text = c.Text,
                        matchesExpected = c.MatchesExpected,
                        score = c.Score,
                        centerX = c.CenterX,
                        centerY = c.CenterY
                    })
                })
            })
        });
        return true;
    }

    private bool HandleGetDimensionSourceDebug(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        int? dimensionId = null;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDimensionId))
            {
                WriteError("dimensionId must be an integer.");
                return true;
            }

            dimensionId = parsedDimensionId;
        }

        var result = api.GetDimensionSourceDebug(viewId, dimensionId);
        WriteJson(new
        {
            viewId = result.ViewId,
            total = result.Total,
            dimensions = result.Dimensions.Select(d => new
            {
                dimensionId = d.DimensionId,
                dimensionType = d.DimensionType,
                teklaDimensionType = d.TeklaDimensionType,
                measuredPoints = d.MeasuredPoints.Select(point => new
                {
                    x = point.X,
                    y = point.Y,
                    order = point.Order
                }),
                pointMappings = d.PointMappings.Select(mapping => new
                {
                    order = mapping.Order,
                    x = mapping.X,
                    y = mapping.Y,
                    status = mapping.Status,
                    matchedOwner = mapping.MatchedOwner,
                    matchedDrawingObjectId = mapping.MatchedDrawingObjectId,
                    matchedModelId = mapping.MatchedModelId,
                    matchedType = mapping.MatchedType,
                    matchedSourceKind = mapping.MatchedSourceKind,
                    distanceToGeometry = mapping.DistanceToGeometry,
                    nearestGeometryPoint = mapping.NearestGeometryPoint == null ? null : new
                    {
                        x = mapping.NearestGeometryPoint.X,
                        y = mapping.NearestGeometryPoint.Y,
                        order = mapping.NearestGeometryPoint.Order
                    },
                    candidateCount = mapping.CandidateCount,
                    warning = mapping.Warning
                }),
                candidates = d.Candidates.Select(c => new
                {
                    owner = c.Owner,
                    type = c.Type,
                    drawingObjectId = c.DrawingObjectId,
                    modelId = c.ModelId,
                    resolvedModelType = c.ResolvedModelType,
                    sourceKind = c.SourceKind,
                    geometrySource = c.GeometrySource,
                    hasGeometry = c.HasGeometry,
                    geometryPointCount = c.GeometryPointCount,
                    geometryBounds = c.GeometryBounds == null ? null : new
                    {
                        minX = c.GeometryBounds.MinX,
                        minY = c.GeometryBounds.MinY,
                        maxX = c.GeometryBounds.MaxX,
                        maxY = c.GeometryBounds.MaxY,
                        width = c.GeometryBounds.Width,
                        height = c.GeometryBounds.Height
                    },
                    geometryPoints = c.GeometryPoints.Select(point => new
                    {
                        x = point.X,
                        y = point.Y,
                        order = point.Order
                    }),
                    geometryWarnings = c.GeometryWarnings
                })
            })
        });
        return true;
    }

    private bool HandleGetDimensionGroupsDebug(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var method = typeof(TeklaDrawingDimensionsApi).GetMethod("GetDimensionGroupReductionDebug", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            WriteError("Internal GetDimensionGroupReductionDebug() was not found.");
            return true;
        }

        object? result;
        try
        {
            result = method.Invoke(api, new object?[] { viewId });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            WriteJson(new
            {
                error = ex.InnerException.Message,
                type = ex.InnerException.GetType().Name,
                stack = ex.InnerException.StackTrace
            });
            return true;
        }

        if (result == null)
        {
            WriteError("Internal GetDimensionGroupReductionDebug() returned null.");
            return true;
        }

        var resultType = result.GetType();
        var groups = resultType.GetProperty("Groups")?.GetValue(result) as System.Collections.IEnumerable;
        if (groups == null)
        {
            WriteError("Reduction debug groups were not found.");
            return true;
        }

        var payload = groups.Cast<object>().Select(group => new
        {
            rawGroup = SerializeGroup(group.GetType().GetProperty("RawGroup")?.GetValue(group)),
            reducedGroup = SerializeGroup(group.GetType().GetProperty("ReducedGroup")?.GetValue(group)),
            reduction = SerializeReductionItems(group.GetType().GetProperty("Items")?.GetValue(group) as System.Collections.IEnumerable),
            packets = SerializeRepresentativePackets(group.GetType().GetProperty("Packets")?.GetValue(group) as System.Collections.IEnumerable),
            combineCandidates = SerializeCombineCandidates(group.GetType().GetProperty("CombineCandidates")?.GetValue(group) as System.Collections.IEnumerable)
        });

        WriteJson(new { groups = payload });
        return true;
    }

    private static IEnumerable<object> SerializeRepresentativePackets(System.Collections.IEnumerable? packets)
    {
        if (packets == null)
            return [];

        return packets.Cast<object>().Select(packet =>
        {
            var type = packet.GetType();
            return new
            {
                packetIndex = type.GetProperty("PacketIndex")?.GetValue(packet),
                startDimensionId = type.GetProperty("StartDimensionId")?.GetValue(packet),
                endDimensionId = type.GetProperty("EndDimensionId")?.GetValue(packet),
                dimensionIds = (type.GetProperty("DimensionIds")?.GetValue(packet) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
                itemCount = type.GetProperty("ItemCount")?.GetValue(packet),
                selectionMode = type.GetProperty("SelectionMode")?.GetValue(packet),
                representativeDimensionId = type.GetProperty("RepresentativeDimensionId")?.GetValue(packet),
                splitGapFromPreviousEndToCurrentStart = type.GetProperty("SplitGapFromPreviousEndToCurrentStart")?.GetValue(packet),
                splitGapFromPreviousStartToCurrentEnd = type.GetProperty("SplitGapFromPreviousStartToCurrentEnd")?.GetValue(packet),
                splitThreshold = type.GetProperty("SplitThreshold")?.GetValue(packet),
                isCombineCandidate = type.GetProperty("IsCombineCandidate")?.GetValue(packet),
                combineConnectivityMode = type.GetProperty("CombineConnectivityMode")?.GetValue(packet),
                blockingReasons = (type.GetProperty("BlockingReasons")?.GetValue(packet) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
                combinePreview = SerializeCombinePreview(type.GetProperty("CombinePreview")?.GetValue(packet))
            };
        });
    }

    private static IEnumerable<object> SerializeCombineCandidates(System.Collections.IEnumerable? candidates)
    {
        if (candidates == null)
            return [];

        return candidates.Cast<object>().Select(candidate =>
        {
            var type = candidate.GetType();
            return new
            {
                dimensionIds = (type.GetProperty("DimensionIds")?.GetValue(candidate) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
                isCombineCandidate = type.GetProperty("IsCombineCandidate")?.GetValue(candidate),
                combineConnectivityMode = type.GetProperty("CombineConnectivityMode")?.GetValue(candidate),
                blockingReasons = (type.GetProperty("BlockingReasons")?.GetValue(candidate) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
                combinePreview = SerializeCombinePreview(type.GetProperty("CombinePreview")?.GetValue(candidate))
            };
        });
    }

    private static object? SerializeCombinePreview(object? preview)
    {
        if (preview == null)
            return null;

        var type = preview.GetType();
        var startPoint = type.GetProperty("StartPoint")?.GetValue(preview);
        var endPoint = type.GetProperty("EndPoint")?.GetValue(preview);

        return new
        {
            baseDimensionId = type.GetProperty("BaseDimensionId")?.GetValue(preview),
            dimensionIds = (type.GetProperty("DimensionIds")?.GetValue(preview) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
            startPoint = SerializePoint(startPoint),
            endPoint = SerializePoint(endPoint),
            pointList = (type.GetProperty("PointList")?.GetValue(preview) as System.Collections.IEnumerable)?.Cast<object>().Select(SerializePoint).ToArray(),
            lengthList = (type.GetProperty("LengthList")?.GetValue(preview) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
            realLengthList = (type.GetProperty("RealLengthList")?.GetValue(preview) as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
            distance = type.GetProperty("Distance")?.GetValue(preview)
        };
    }

    private static object? SerializePoint(object? point)
    {
        if (point == null)
            return null;

        var type = point.GetType();
        return new
        {
            x = type.GetProperty("X")?.GetValue(point),
            y = type.GetProperty("Y")?.GetValue(point),
            order = type.GetProperty("Order")?.GetValue(point)
        };
    }

    private bool HandleGetDimensionArrangementDebug(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var targetGap = 10.0;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out targetGap) || targetGap < 0)
            {
                WriteError("targetGap must be a non-negative number.");
                return true;
            }
        }

        var method = typeof(TeklaDrawingDimensionsApi).GetMethod("GetDimensionArrangementDebug", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            WriteError("Internal GetDimensionArrangementDebug() was not found.");
            return true;
        }

        var result = method.Invoke(api, new object?[] { viewId, targetGap }) as DimensionArrangementDebugResult;
        if (result == null)
        {
            WriteError("Internal GetDimensionArrangementDebug() returned null.");
            return true;
        }

        WriteDimensionArrangementDebugResult(result);
        return true;
    }

    private bool HandleArrangeDimensions(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var targetGap = 10.0;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out targetGap) || targetGap < 0)
            {
                WriteError("targetGap must be a non-negative number.");
                return true;
            }
        }

        var result = api.ArrangeDimensions(viewId, targetGap);
        WriteArrangeDimensionsResult(result);
        return true;
    }

    private bool HandleCombineDimensions(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseCombineDimensionsRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.CombineDimensions(
            parseResult.Request.ViewId,
            parseResult.Request.DimensionIds,
            parseResult.Request.PreviewOnly);
        WriteCombineDimensionsResult(result);
        return true;
    }

    private bool HandleMoveDimension(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseMoveDimensionRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.MoveDimension(parseResult.Request.DimensionId, parseResult.Request.Delta);
        WriteMoveDimensionResult(result);
        return true;
    }

    private bool HandleCreateDimension(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseCreateDimensionRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.CreateDimension(
            parseResult.Request.ViewId,
            parseResult.Request.Points,
            parseResult.Request.Direction,
            parseResult.Request.Distance,
            parseResult.Request.AttributesFile);
        WriteCreateDimensionResult(result);
        return true;
    }

    private bool HandleDeleteDimension(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseDeleteDimensionRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.DeleteDimension(parseResult.Request.DimensionId);
        if (!result.HasActiveDrawing)
        {
            WriteRawJson(NoActiveDrawingErrorJson);
            return true;
        }

        WriteDeleteDimensionResult(result);
        return true;
    }

    private bool HandlePlaceControlDiagonals(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParsePlaceControlDiagonalsRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.PlaceControlDiagonals(
            parseResult.Request.ViewId,
            parseResult.Request.Distance,
            parseResult.Request.AttributesFile,
            parseResult.Request.IncludeMaterialTypes);

        WriteJson(new
        {
            created = result.Created,
            createdCount = result.CreatedCount,
            viewId = result.ViewId,
            viewType = result.ViewType,
            rectangleLike = result.RectangleLike,
            requestedDiagonalCount = result.RequestedDiagonalCount,
            partsScanned = result.PartsScanned,
            sourceDimensionsScanned = result.SourceDimensionsScanned,
            candidatePoints = result.CandidatePoints,
            dimensionId = result.DimensionId,
            dimensionIds = result.DimensionIds,
            startPoint = result.StartPoint,
            endPoint = result.EndPoint,
            farthestDistance = result.FarthestDistance,
            selectViewMs = result.SelectViewMs,
            readGeometryMs = result.ReadGeometryMs,
            findExtremesMs = result.FindExtremesMs,
            createMs = result.CreateMs,
            commitMs = result.CommitMs,
            totalMs = result.TotalMs,
            error = result.Error
        });
        return true;
    }

    private void WriteGetDimensionsResult(GetDimensionsResult result)
    {
        static object? SerializeLine(DrawingLineInfo? line)
        {
            if (line == null)
                return null;

            return new
            {
                startX = line.StartX,
                startY = line.StartY,
                endX = line.EndX,
                endY = line.EndY,
                length = line.Length
            };
        }

        WriteJson(new
        {
            total = result.Total,
            drawingDimensionCount = result.DrawingDimensionCount,
            rawItemCount = result.RawItemCount,
            reducedItemCount = result.ReducedItemCount,
            groupCount = result.GroupCount,
            groups = result.Groups.Select(g => new
            {
                viewId = g.ViewId,
                viewType = g.ViewType,
                dimensionType = g.DimensionType,
                teklaDimensionType = g.TeklaDimensionType,
                direction = g.Direction == null ? null : new
                {
                    x = g.Direction.X,
                    y = g.Direction.Y
                },
                topDirection = g.TopDirection,
                referenceLine = SerializeLine(g.ReferenceLine),
                leadLineMain = SerializeLine(g.LeadLineMain),
                leadLineSecond = SerializeLine(g.LeadLineSecond),
                maximumDistance = g.MaximumDistance,
                rawItemCount = g.RawItemCount,
                reducedItemCount = g.ReducedItemCount,
                items = g.Items.Select(item => new
                {
                    id = item.Id,
                    segmentIds = item.SegmentIds,
                    viewId = item.ViewId,
                    dimensionType = item.DimensionType,
                    teklaDimensionType = item.TeklaDimensionType,
                    referenceLine = SerializeLine(item.ReferenceLine),
                    startPoint = item.StartPoint == null ? null : new
                    {
                        x = item.StartPoint.X,
                        y = item.StartPoint.Y,
                        order = item.StartPoint.Order
                    },
                    endPoint = item.EndPoint == null ? null : new
                    {
                        x = item.EndPoint.X,
                        y = item.EndPoint.Y,
                        order = item.EndPoint.Order
                    },
                    centerPoint = item.CenterPoint == null ? null : new
                    {
                        x = item.CenterPoint.X,
                        y = item.CenterPoint.Y,
                        order = item.CenterPoint.Order
                    },
                    pointList = item.PointList.Select(p => new
                    {
                        x = p.X,
                        y = p.Y,
                        order = p.Order
                    }),
                    lengthList = item.LengthList,
                    realLengthList = item.RealLengthList,
                    distance = item.Distance
                })
            })
        });
    }

    private void WriteMoveDimensionResult(MoveDimensionResult result)
    {
        WriteJson(new
        {
            moved = result.Moved,
            dimensionId = result.DimensionId,
            newDistance = result.NewDistance
        });
    }

    private void WriteCreateDimensionResult(CreateDimensionResult result)
    {
        WriteJson(new
        {
            created = result.Created,
            dimensionId = result.DimensionId,
            viewId = result.ViewId,
            pointCount = result.PointCount,
            error = result.Error
        });
    }

    private void WriteDeleteDimensionResult(DeleteDimensionResult result)
    {
        WriteJson(new
        {
            deleted = result.Deleted,
            dimensionId = result.DimensionId
        });
    }

    private void WriteArrangeDimensionsResult(ArrangeDimensionsResult result)
    {
        WriteJson(new
        {
            appliedCount = result.AppliedCount,
            skippedCount = result.SkippedCount,
            applied = result.Applied.Select(item => new
            {
                dimensionId = item.DimensionId,
                distanceDelta = item.DistanceDelta,
                newDistance = item.NewDistance
            }),
            skipReasons = result.SkipReasons
        });
    }

    private void WriteCombineDimensionsResult(CombineDimensionsResult result)
    {
        WriteJson(new
        {
            previewOnly = result.PreviewOnly,
            candidateCount = result.CandidateCount,
            combinedCount = result.CombinedCount,
            skippedCount = result.SkippedCount,
            combined = result.Combined.Select(item => new
            {
                viewId = item.ViewId,
                viewType = item.ViewType,
                dimensionType = item.DimensionType,
                packetIndex = item.PacketIndex,
                baseDimensionId = item.BaseDimensionId,
                connectivityMode = item.ConnectivityMode,
                previewOnly = item.PreviewOnly,
                combined = item.Combined,
                createdDimensionId = item.CreatedDimensionId,
                distance = item.Distance,
                reason = item.Reason,
                dimensionIds = item.DimensionIds,
                deletedDimensionIds = item.DeletedDimensionIds,
                blockingReasons = item.BlockingReasons,
                pointList = item.PointList.Select(point => new
                {
                    x = point.X,
                    y = point.Y,
                    order = point.Order
                })
            }),
            skipped = result.Skipped.Select(item => new
            {
                viewId = item.ViewId,
                viewType = item.ViewType,
                dimensionType = item.DimensionType,
                packetIndex = item.PacketIndex,
                baseDimensionId = item.BaseDimensionId,
                connectivityMode = item.ConnectivityMode,
                previewOnly = item.PreviewOnly,
                combined = item.Combined,
                createdDimensionId = item.CreatedDimensionId,
                distance = item.Distance,
                reason = item.Reason,
                dimensionIds = item.DimensionIds,
                deletedDimensionIds = item.DeletedDimensionIds,
                blockingReasons = item.BlockingReasons,
                pointList = item.PointList.Select(point => new
                {
                    x = point.X,
                    y = point.Y,
                    order = point.Order
                })
            })
        });
    }

    private void WriteDimensionArrangementDebugResult(DimensionArrangementDebugResult result)
    {
        static object? SerializeLine(DrawingLineInfo? line)
        {
            if (line == null)
                return null;

            return new
            {
                startX = line.StartX,
                startY = line.StartY,
                endX = line.EndX,
                endY = line.EndY,
                length = line.Length
            };
        }

        static object? SerializeBounds(DrawingBoundsInfo? bounds)
        {
            if (bounds == null)
                return null;

            return new
            {
                minX = bounds.MinX,
                minY = bounds.MinY,
                maxX = bounds.MaxX,
                maxY = bounds.MaxY,
                width = bounds.Width,
                height = bounds.Height
            };
        }

        WriteJson(new
        {
            rawViewFilteredTotal = result.RawViewFilteredTotal,
            viewFilteredTotal = result.ViewFilteredTotal,
            rawGroupCount = result.RawGroupCount,
            groupCount = result.GroupCount,
            dedupRejectedCount = result.DedupRejectedCount,
            targetGapPaper = result.TargetGapPaper,
            dedup = result.Dedup.Select(group => new
            {
                viewId = group.ViewId,
                viewType = group.ViewType,
                dimensionType = group.DimensionType,
                rawMemberCount = group.RawMemberCount,
                reducedMemberCount = group.ReducedMemberCount,
                rejectedCount = group.RejectedCount,
                items = group.Items.Select(item => new
                {
                    dimensionId = item.DimensionId,
                    sourceKind = item.SourceKind,
                    geometryKind = item.GeometryKind,
                    status = item.Status,
                    reason = item.Reason,
                    representativeDimensionId = item.RepresentativeDimensionId
                })
            }),
            groups = result.Groups.Select(group => new
            {
                viewId = group.ViewId,
                viewType = group.ViewType,
                dimensionType = group.DimensionType,
                orientation = group.Orientation,
                directionX = group.DirectionX,
                directionY = group.DirectionY,
                topDirection = group.TopDirection,
                referenceLine = SerializeLine(group.ReferenceLine),
                memberCount = group.MemberCount,
                maximumDistance = group.MaximumDistance,
                bounds = SerializeBounds(group.Bounds),
                groupingBasis = group.GroupingBasis,
                members = group.Members.Select(member => new
                {
                    dimensionId = member.DimensionId,
                    distance = member.Distance,
                    sortKey = member.SortKey,
                    directionX = member.DirectionX,
                    directionY = member.DirectionY,
                    topDirection = member.TopDirection,
                    bounds = SerializeBounds(member.Bounds),
                    referenceLine = SerializeLine(member.ReferenceLine),
                    leadLineMain = SerializeLine(member.LeadLineMain),
                    leadLineSecond = SerializeLine(member.LeadLineSecond)
                })
            }),
            stacks = result.Stacks.Select(stack => new
            {
                viewId = stack.ViewId,
                viewType = stack.ViewType,
                dimensionType = stack.DimensionType,
                orientation = stack.Orientation,
                directionX = stack.DirectionX,
                directionY = stack.DirectionY,
                topDirection = stack.TopDirection,
                referenceLine = SerializeLine(stack.ReferenceLine),
                alignmentApplied = stack.AlignmentApplied,
                groupingBasis = stack.GroupingBasis,
                members = stack.Members.Select(member => new
                {
                    dimensionId = member.DimensionId,
                    dimensionType = member.DimensionType,
                    orientation = member.Orientation,
                    distance = member.Distance,
                    normalizationDelta = member.NormalizationDelta,
                    normalizationStatus = member.NormalizationStatus,
                    normalizationReason = member.NormalizationReason,
                    referenceLine = SerializeLine(member.ReferenceLine),
                    planningReferenceLine = SerializeLine(member.PlanningReferenceLine),
                    alignmentClusterId = member.AlignmentClusterId,
                    alignmentAnchorDimensionId = member.AlignmentAnchorDimensionId,
                    alignmentStatus = member.AlignmentStatus,
                    alignmentReason = member.AlignmentReason
                }),
                alignmentClusters = stack.AlignmentClusters.Select(cluster => new
                {
                    clusterId = cluster.ClusterId,
                    anchorDimensionId = cluster.AnchorDimensionId,
                    anchorReferenceLine = SerializeLine(cluster.AnchorReferenceLine),
                    anchorDistance = cluster.AnchorDistance,
                    distanceSpread = cluster.DistanceSpread,
                    applied = cluster.Applied,
                    normalizationApplied = cluster.NormalizationApplied,
                    normalizationThreshold = cluster.NormalizationThreshold,
                    normalizationReason = cluster.NormalizationReason,
                    status = cluster.Status,
                    reason = cluster.Reason,
                    dimensionIds = cluster.DimensionIds,
                    members = cluster.Members.Select(member => new
                    {
                        dimensionId = member.DimensionId,
                        currentDistance = member.CurrentDistance,
                        normalizationDelta = member.NormalizationDelta
                    })
                })
            }),
            spacing = result.Spacing.Select(info => new
            {
                viewId = info.ViewId,
                viewType = info.ViewType,
                dimensionType = info.DimensionType,
                orientation = info.Orientation,
                directionX = info.DirectionX,
                directionY = info.DirectionY,
                topDirection = info.TopDirection,
                referenceLine = SerializeLine(info.ReferenceLine),
                hasOverlaps = info.HasOverlaps,
                minimumDistance = info.MinimumDistance,
                pairs = info.Pairs.Select(pair => new
                {
                    firstDimensionId = pair.FirstDimensionId,
                    secondDimensionId = pair.SecondDimensionId,
                    distance = pair.Distance,
                    isOverlap = pair.IsOverlap
                })
            }),
            plans = result.Plans.Select(plan => new
            {
                viewId = plan.ViewId,
                viewType = plan.ViewType,
                dimensionType = plan.DimensionType,
                orientation = plan.Orientation,
                directionX = plan.DirectionX,
                directionY = plan.DirectionY,
                topDirection = plan.TopDirection,
                referenceLine = SerializeLine(plan.ReferenceLine),
                targetGapPaper = plan.TargetGapPaper,
                targetGapDrawing = plan.TargetGapDrawing,
                proposalCount = plan.ProposalCount,
                hasApplicableChanges = plan.HasApplicableChanges,
                proposals = plan.Proposals.Select(proposal => new
                {
                    dimensionId = proposal.DimensionId,
                    currentDistance = proposal.CurrentDistance,
                    axisShift = proposal.AxisShift,
                    normalizationDelta = proposal.NormalizationDelta,
                    spacingDelta = proposal.SpacingDelta,
                    distanceDelta = proposal.DistanceDelta,
                    targetDistance = proposal.TargetDistance,
                    canApply = proposal.CanApply,
                    reason = proposal.Reason
                })
            })
        });
    }

    private static object? SerializeDirection(object? direction)
    {
        if (direction == null)
            return null;

        var type = direction.GetType();
        var xField = type.GetField("Item1");
        var yField = type.GetField("Item2");
        var xProp = type.GetProperty("X");
        var yProp = type.GetProperty("Y");
        var x = xProp?.GetValue(direction) ?? xField?.GetValue(direction);
        var y = yProp?.GetValue(direction) ?? yField?.GetValue(direction);
        return new { x, y };
    }

    private static object SerializeMembers(System.Collections.IEnumerable? members)
    {
        if (members == null)
            return System.Array.Empty<object>();

        return members.Cast<object>().Select(member => new
        {
            dimensionId = member.GetType().GetProperty("DimensionId")?.GetValue(member),
            segmentId = member.GetType().GetProperty("SegmentId")?.GetValue(member),
            sourceKind = member.GetType().GetProperty("SourceKind")?.GetValue(member),
            geometryKind = member.GetType().GetProperty("GeometryKind")?.GetValue(member),
            startX = member.GetType().GetProperty("StartX")?.GetValue(member),
            startY = member.GetType().GetProperty("StartY")?.GetValue(member),
            endX = member.GetType().GetProperty("EndX")?.GetValue(member),
            endY = member.GetType().GetProperty("EndY")?.GetValue(member),
            distance = member.GetType().GetProperty("Distance")?.GetValue(member),
            directionX = member.GetType().GetProperty("DirectionX")?.GetValue(member),
            directionY = member.GetType().GetProperty("DirectionY")?.GetValue(member),
            topDirection = member.GetType().GetProperty("TopDirection")?.GetValue(member),
            sortKey = member.GetType().GetProperty("SortKey")?.GetValue(member),
            bounds = SerializeBounds(member.GetType().GetProperty("Bounds")?.GetValue(member) as DrawingBoundsInfo),
            referenceLine = SerializeLine(member.GetType().GetProperty("ReferenceLine")?.GetValue(member) as DrawingLineInfo),
            leadLineMain = SerializeLine(member.GetType().GetProperty("LeadLineMain")?.GetValue(member) as DrawingLineInfo),
            leadLineSecond = SerializeLine(member.GetType().GetProperty("LeadLineSecond")?.GetValue(member) as DrawingLineInfo)
        });

        static object? SerializeLine(DrawingLineInfo? line)
        {
            if (line == null)
                return null;

            return new
            {
                startX = line.StartX,
                startY = line.StartY,
                endX = line.EndX,
                endY = line.EndY,
                length = line.Length
            };
        }

        static object? SerializeBounds(DrawingBoundsInfo? bounds)
        {
            if (bounds == null)
                return null;

            return new
            {
                minX = bounds.MinX,
                minY = bounds.MinY,
                maxX = bounds.MaxX,
                maxY = bounds.MaxY,
                width = bounds.Width,
                height = bounds.Height
            };
        }
    }

    private static object? SerializeGroup(object? group)
    {
        if (group == null)
            return null;

        return new
        {
            viewId = group.GetType().GetProperty("ViewId")?.GetValue(group),
            viewType = group.GetType().GetProperty("ViewType")?.GetValue(group),
            dimensionType = group.GetType().GetProperty("DimensionType")?.GetValue(group),
            sourceKind = group.GetType().GetProperty("SourceKind")?.GetValue(group),
            geometryKind = group.GetType().GetProperty("GeometryKind")?.GetValue(group),
            orientation = group.GetType().GetProperty("Orientation")?.GetValue(group),
            topDirection = group.GetType().GetProperty("TopDirection")?.GetValue(group),
            direction = SerializeDirection(group.GetType().GetProperty("Direction")?.GetValue(group)),
            referenceLine = SerializeDebugLine(group.GetType().GetProperty("ReferenceLine")?.GetValue(group) as DrawingLineInfo),
            bounds = SerializeDebugBounds(group.GetType().GetProperty("Bounds")?.GetValue(group) as DrawingBoundsInfo),
            maximumDistance = group.GetType().GetProperty("MaximumDistance")?.GetValue(group),
            rawItemCount = group.GetType().GetProperty("RawItemCount")?.GetValue(group),
            reducedItemCount = group.GetType().GetProperty("ReducedItemCount")?.GetValue(group),
            members = SerializeMembers(group.GetType().GetProperty("Members")?.GetValue(group) as System.Collections.IEnumerable)
        };
    }

    private static object SerializeReductionItems(System.Collections.IEnumerable? items)
    {
        if (items == null)
            return System.Array.Empty<object>();

        return items.Cast<object>().Select(item => new
        {
            status = item.GetType().GetProperty("Status")?.GetValue(item),
            reason = item.GetType().GetProperty("Reason")?.GetValue(item),
            packetIndex = item.GetType().GetProperty("PacketIndex")?.GetValue(item),
            representativeDimensionId = item.GetType().GetProperty("RepresentativeDimensionId")?.GetValue(item),
            role = GetContextPropertyValue(item, "Role"),
            hasSourceGeometry = GetContextPropertyValue(item, "HasSourceGeometry"),
            sourceObjectIds = (GetContextPropertyValue(item, "SourceObjectIds") as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
            localBounds = SerializeDebugBounds(GetContextPropertyValue(item, "LocalBounds") as DrawingBoundsInfo),
            geometryWarnings = (GetContextPropertyValue(item, "GeometryWarnings") as System.Collections.IEnumerable)?.Cast<object>().ToArray(),
            member = SerializeMembers(new[]
            {
                item.GetType().GetProperty("Item")?.GetValue(item)
            }.Where(static value => value != null)) as object
        });
    }

    private static object? GetContextPropertyValue(object item, string propertyName)
    {
        var context = item.GetType().GetProperty("Context")?.GetValue(item);
        if (context == null)
            return null;

        return context.GetType().GetProperty(propertyName)?.GetValue(context);
    }

    private static object? SerializeDebugLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return new
        {
            startX = line.StartX,
            startY = line.StartY,
            endX = line.EndX,
            endY = line.EndY,
            length = line.Length
        };
    }

    private static object? SerializeDebugBounds(DrawingBoundsInfo? bounds)
    {
        if (bounds == null)
            return null;

        return new
        {
            minX = bounds.MinX,
            minY = bounds.MinY,
            maxX = bounds.MaxX,
            maxY = bounds.MaxY,
            width = bounds.Width,
            height = bounds.Height
        };
    }
}
