using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleGeometryCommands(string command, string[] args)
    {
        TeklaDrawingPartGeometryApi? partGeometryApi = null;
        TeklaDrawingPartGeometryApi GetPartGeometryApi() => partGeometryApi ??= new TeklaDrawingPartGeometryApi(_model);
        TeklaDrawingPartSolidGeometryApi? partSolidGeometryApi = null;
        TeklaDrawingPartSolidGeometryApi GetPartSolidGeometryApi() =>
            partSolidGeometryApi ??= new TeklaDrawingPartSolidGeometryApi(_model);
        TeklaDrawingPartCandidatePointApi? partCandidatePointApi = null;
        TeklaDrawingPartCandidatePointApi GetPartCandidatePointApi() =>
            partCandidatePointApi ??= new TeklaDrawingPartCandidatePointApi(_model);
        TeklaDrawingPartPointApi? partPointApi = null;
        TeklaDrawingPartPointApi GetPartPointApi() => partPointApi ??= new TeklaDrawingPartPointApi(_model, GetPartGeometryApi());
        TeklaDrawingAssemblyOutlineApi? assemblyOutlineApi = null;
        TeklaDrawingAssemblyOutlineApi GetAssemblyOutlineApi() => assemblyOutlineApi ??= new TeklaDrawingAssemblyOutlineApi(_model);
        TeklaDrawingGridApi? gridApi = null;
        TeklaDrawingGridApi GetGridApi() => gridApi ??= new TeklaDrawingGridApi();
        TeklaDrawingViewContextApi? viewContextApi = null;
        TeklaDrawingViewContextApi GetViewContextApi() => viewContextApi ??= new TeklaDrawingViewContextApi(_model);
        TeklaDrawingPartsApi? partsApi = null;
        TeklaDrawingPartsApi GetPartsApi() => partsApi ??= new TeklaDrawingPartsApi(_model);
        TeklaDrawingMarkApi? markApi = null;
        TeklaDrawingMarkApi GetMarkApi() => markApi ??= new TeklaDrawingMarkApi(_model);
        TeklaDrawingDebugOverlayApi? debugOverlayApi = null;
        TeklaDrawingDebugOverlayApi GetDebugOverlayApi() => debugOverlayApi ??= new TeklaDrawingDebugOverlayApi();

        switch (command)
        {
            case "get_part_geometry_in_view":
                return HandleGetPartGeometryInView(GetPartGeometryApi(), args);

            case "get_all_parts_geometry_in_view":
                return HandleGetAllPartsGeometryInView(GetPartGeometryApi(), args);

            case "get_part_solid_geometry_in_view":
                return HandleGetPartSolidGeometryInView(GetPartSolidGeometryApi(), args);

            case "get_part_candidate_points_in_view":
                return HandleGetPartCandidatePointsInView(GetPartCandidatePointApi(), args);

            case "get_part_points_in_view":
                return HandleGetPartPointsInView(GetPartPointApi(), args);

            case "get_all_part_points_in_view":
                return HandleGetAllPartPointsInView(GetPartPointApi(), args);

            // Test-only probe for validating the two Clipper2 unions on a real view.
            case "get_assembly_outline":
                return HandleGetAssemblyOutline(GetAssemblyOutlineApi(), args);

            case "get_structural_outline":
                return HandleGetStructuralOutline(args);

            case "get_contact_candidate_points":
                return HandleGetContactCandidatePoints(GetDebugOverlayApi(), args);

            case "get_grid_axes":
                return HandleGetGridAxes(GetGridApi(), args);

            case "get_drawing_view_context":
                return HandleGetDrawingViewContext(GetViewContextApi(), args);

            case "get_drawing_parts":
                return HandleGetDrawingParts(GetPartsApi());

            case "draw_debug_overlay":
                return HandleDrawDebugOverlay(GetDebugOverlayApi(), args);

            case "draw_mark_boxes":
                return HandleDrawMarkBoxes(GetMarkApi(), GetDebugOverlayApi(), args);

            case "draw_selected_mark_text_boxes":
                return HandleDrawSelectedMarkTextBoxes(GetDebugOverlayApi());

            case "draw_selected_mark_resolved_geometry":
            case "draw_selected_mark_object_aligned_box":
                return HandleDrawSelectedMarkObjectAlignedBox(GetDebugOverlayApi());

            case "draw_selected_mark_part_axis_geometry":
                return HandleDrawSelectedMarkPartAxisGeometry(GetPartGeometryApi(), GetDebugOverlayApi());

            case "clear_debug_overlay":
                return HandleClearDebugOverlay(GetDebugOverlayApi(), args);

            default:
                return false;
        }
    }

    private bool HandleGetAllPartsGeometryInView(TeklaDrawingPartGeometryApi api, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
        {
            WriteError("get_all_parts_geometry_in_view requires viewId argument");
            return true;
        }
        var results = api.GetAllPartsGeometryInView(viewId);
        WriteJson(new
        {
            viewId,
            total = results.Count,
            parts = results.Select(r => new
            {
                modelId    = r.ModelId,
                type       = r.Type,
                name       = r.Name,
                partPos    = r.PartPos,
                profile    = r.Profile,
                material   = r.Material,
                startPoint = r.StartPoint,
                endPoint   = r.EndPoint,
                axisX      = r.AxisX,
                axisY      = r.AxisY,
                bboxMin    = r.BboxMin,
                bboxMax    = r.BboxMax,
                solidVertices = r.SolidVertices,
                viewHull   = r.ViewHull,
                solidGeometryComplete = r.SolidGeometryComplete,
                materialType = r.MaterialType,
                partPrefix   = r.PartPrefix,
                role         = r.Role.Role.ToString(),
                roleRuleId   = r.Role.RuleId,
                roleReason   = r.Role.Reason
            })
        });
        return true;
    }

    private bool HandleGetPartSolidGeometryInView(TeklaDrawingPartSolidGeometryApi api, string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], out var viewId)
            || !int.TryParse(args[2], out var modelId))
        {
            WriteError("get_part_solid_geometry_in_view requires viewId and modelId arguments");
            return true;
        }

        WritePartSolidGeometryInViewResult(api.GetPartSolidGeometryInView(viewId, modelId));
        return true;
    }

    private bool HandleGetPartCandidatePointsInView(TeklaDrawingPartCandidatePointApi api, string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], out var viewId)
            || !int.TryParse(args[2], out var modelId))
        {
            WriteError("get_part_candidate_points_in_view requires viewId and modelId arguments");
            return true;
        }

        WritePartCandidatePointsInViewResult(api.GetPartCandidatePointsInView(viewId, modelId));
        return true;
    }

    private bool HandleGetAllPartPointsInView(TeklaDrawingPartPointApi api, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
        {
            WriteError("get_all_part_points_in_view requires viewId argument");
            return true;
        }

        var results = api.GetAllPartPointsInView(viewId);
        WriteJson(new
        {
            viewId,
            total = results.Count,
            parts = results.Select(r => new
            {
                modelId = r.ModelId,
                type = r.Type,
                name = r.Name,
                partPos = r.PartPos,
                profile = r.Profile,
                material = r.Material,
                points = r.Points.Select(p => new
                {
                    kind = p.Kind.ToString(),
                    sourceKind = p.SourceKind.ToString(),
                    index = p.Index,
                    point = p.Point
                })
            })
        });
        return true;
    }

    /// <summary>
    /// Model ids from the argument, or null when the caller named none.
    ///
    /// A token that is not a number is refused rather than skipped. Skipping it turns a
    /// typo into a full outline that looks like a successful answer, and a full extent is
    /// exactly the wrong number to hand back by accident - it is longer than the frame by
    /// whatever overhangs it.
    /// </summary>
    private static bool TryParseModelIds(string argument, out IReadOnlyCollection<int>? modelIds, out string? error)
    {
        modelIds = null;
        error = null;

        var tokens = argument.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return true;

        var ids = new HashSet<int>();
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var id))
            {
                error = $"'{token}' is not a model id";
                return false;
            }

            ids.Add(id);
        }

        modelIds = ids;
        return true;
    }

    /// <summary>
    /// Where the parts drawn in a view touch each other, as places a dimension could be
    /// taken to, and optionally drawn on the sheet so a person can check them by eye.
    /// </summary>
    private bool HandleGetContactCandidatePoints(TeklaDrawingDebugOverlayApi overlay, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
        {
            WriteError("get_contact_candidate_points requires viewId argument");
            return true;
        }

        var draw = args.Length > 2 &&
            args[2].Equals("true", StringComparison.OrdinalIgnoreCase);

        var contacts = new TeklaDrawingViewContactApi(_model).GetContactGraph(viewId);
        var geometry = ContactGeometryInViewBuilder.Build(contacts);
        var result = DrawingContactCandidatePointBuilder.Build(geometry);

        var drawn = draw ? Draw(overlay, viewId, geometry, result) : 0;

        WriteJson(new
        {
            success = result.Error == null,
            viewId,
            isComplete = result.IsComplete,
            searchComplete = result.SearchComplete,
            error = result.Error,
            drawnCount = drawn,

            pointCount = result.Points.Count,
            points = result.Points.Select(point => new
            {
                modelObjectIds = point.ModelObjectIds,
                point = point.Point,
                source = point.Source.ToString(),
                confidence = point.Confidence.ToString(),
                anchorKind = point.Anchor.Kind.ToString(),
                anchorKey = point.Anchor.Key,
                reason = point.Reason.Code,
                values = point.Reason.Values
            }),

            // The shapes the points came from, so a reader can see a segment for what it is
            // - a patch the view looks along - rather than guessing from two loose points.
            shapes = geometry.Shapes.Select(shape => new
            {
                contactId = shape.ContactId,
                shapeId = shape.ShapeId,
                contactKind = shape.Kind.ToString(),
                shapeKind = shape.Shape.Kind.ToString(),
                modelObjectIds = shape.Participants.ModelObjectIds,
                points = shape.Shape.Points.Select(point => new[] { point.X, point.Y })
            }),

            // Three ways a place can be missing, kept apart because they are answered
            // differently: geometry that never arrived, a region that flattened to nothing,
            // and a shape whose two parts could not both be named.
            unread = result.Unread.Select(part => new { modelId = part.ModelId, reason = part.Reason }),
            unflattened = result.Unflattened.Select(region => new
            {
                contactId = region.ContactId,
                regionIndex = region.RegionIndex,
                parts = region.Participants.ToString(),
                reason = region.Reason
            }),
            unresolved = result.Unresolved.Select(shape => new
            {
                contactId = shape.ContactId,
                solidAId = shape.Participants.SolidAId,
                solidBId = shape.Participants.SolidBId
            })
        });

        return true;
    }

    /// <summary>
    /// Paints the candidates and the shapes they came from into the view.
    ///
    /// The shape as well as the points, because two crosses on a line say nothing about
    /// whether the line was there; drawn together, a patch seen edge-on is visibly one
    /// line with two ends rather than two unexplained marks.
    /// </summary>
    private static int Draw(
        TeklaDrawingDebugOverlayApi overlay,
        int viewId,
        ViewContactGeometryResult geometry,
        ViewContactCandidatePointsResult result)
    {
        var request = new DrawingDebugOverlayRequest
        {
            Group = "contact_candidates",
            ClearGroupFirst = true
        };

        foreach (var shape in geometry.Shapes)
        {
            var points = shape.Shape.Points.Select(point => new[] { point.X, point.Y }).ToList();

            if (points.Count >= 3)
            {
                request.Shapes.Add(new DrawingDebugShape
                {
                    Kind = "polygon", ViewId = viewId, Points = points, Color = "green"
                });
            }
            else if (points.Count == 2)
            {
                request.Shapes.Add(new DrawingDebugShape
                {
                    Kind = "line", ViewId = viewId, Color = "green",
                    X1 = points[0][0], Y1 = points[0][1], X2 = points[1][0], Y2 = points[1][1]
                });
            }
        }

        foreach (var point in result.Points)
        {
            request.Shapes.Add(new DrawingDebugShape
            {
                Kind = "cross", ViewId = viewId, Color = "red", Size = 5,
                X1 = point.Point[0], Y1 = point.Point[1]
            });
        }

        if (request.Shapes.Count == 0)
            return 0;

        return overlay.DrawOverlay(JsonSerializer.Serialize(request)).CreatedCount;
    }

    /// <summary>
    /// The outline of the parts that fix the assembly's size, chosen by role rather than by
    /// a list of ids the caller had to work out first.
    /// </summary>
    private bool HandleGetStructuralOutline(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
        {
            WriteError("get_structural_outline requires viewId argument");
            return true;
        }

        var model = new Tekla.Structures.Model.Model();
        var api = new TeklaDrawingStructuralOutlineApi(
            new TeklaDrawingPartRoleApi(model),
            new TeklaDrawingAssemblyOutlineApi(model));

        var result = api.Get(viewId);

        WriteJson(new
        {
            success = result.Outline.Error == null,
            viewId,
            isComplete = result.IsComplete,

            // A sentence for a person, and the same facts in full for a caller. The
            // sentence caps its lists at five, so anything acting on the result has to read
            // the arrays instead of parsing the prose.
            reservation = result.Reservation(),

            defining = result.Defining.Select(part => new { modelId = part.ModelId, partPos = part.PartPos }),
            unknown = result.Unknown.Select(part => new { modelId = part.ModelId, partPos = part.PartPos }),
            unclassified = result.Unclassified.Select(part => new { modelId = part.ModelId, partPos = part.PartPos }),

            unreadRoles = result.UnreadRoles.Select(part => new { modelId = part.ModelId, reason = part.Reason }),
            unreadOutlineParts = result.Outline.Unread.Select(part => new { modelId = part.ModelId, reason = part.Reason }),
            notVisibleDefiningIds = result.Outline.NotVisibleRequestedIds,

            error = result.Outline.Error,
            assemblyOutline = result.Outline.AssemblyNodes
        });
        return true;
    }

    private bool HandleGetAssemblyOutline(TeklaDrawingAssemblyOutlineApi api, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
        {
            WriteError("get_assembly_outline requires viewId argument");
            return true;
        }

        // An optional list of model ids: "the outline of these parts". One mechanism for
        // both directions - leave the insulation out, or ask for the frame alone - and the
        // geometry stays ignorant of what a part is for.
        IReadOnlyCollection<int>? modelIds = null;
        if (args.Length >= 3 && !TryParseModelIds(args[2], out modelIds, out var idError))
        {
            WriteError($"get_assembly_outline: {idError}");
            return true;
        }

        var result = api.GetAssemblyOutline(viewId, options: null, modelIds);
        WriteJson(new
        {
            success = result.Error == null,
            viewId = result.ViewId,
            isComplete = result.IsComplete,
            restricted = result.Restricted,
            selectionComplete = result.SelectionComplete,
            visibleCount = result.VisibleCount,
            requestedIds = result.RequestedIds,
            notVisibleRequestedIds = result.NotVisibleRequestedIds,
            error = result.Error,
            assemblyOutline = result.AssemblyNodes,
            partOutlines = result.PartNodes.Select(part => new
            {
                modelId = part.Key,
                outline = part.Value
            }),
            unread = result.Unread.Select(part => new { modelId = part.ModelId, reason = part.Reason })
        });
        return true;
    }

    private bool HandleGetPartGeometryInView(TeklaDrawingPartGeometryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParsePartGeometryInViewRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.GetPartGeometryInView(
            parseResult.Request.ViewId,
            parseResult.Request.ModelId);
        WritePartInView(result);
        return true;
    }

    private bool HandleGetPartPointsInView(TeklaDrawingPartPointApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParsePartPointsInViewRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.GetPartPointsInView(
            parseResult.Request.ViewId,
            parseResult.Request.ModelId);
        WritePartPointsInViewResult(result);
        return true;
    }

    private bool HandleGetGridAxes(TeklaDrawingGridApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseGridAxesRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.GetGridAxes(parseResult.Request.ViewId);
        WriteGridAxesResult(result);
        return true;
    }

    private bool HandleGetDrawingParts(TeklaDrawingPartsApi api)
    {
        var result = api.GetDrawingParts();
        WriteGetDrawingPartsResult(result);
        return true;
    }

    private bool HandleGetDrawingViewContext(TeklaDrawingViewContextApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseDrawingViewContextRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.GetViewContext(parseResult.Request.ViewId);
        WriteJson(result);
        return true;
    }

    private bool HandleDrawDebugOverlay(TeklaDrawingDebugOverlayApi api, string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            WriteError("draw_debug_overlay requires a JSON payload argument");
            return true;
        }

        var result = api.DrawOverlay(args[1]);
        WriteJson(new
        {
            group = result.Group,
            clearedCount = result.ClearedCount,
            createdCount = result.CreatedCount,
            createdIds = result.CreatedIds
        });
        return true;
    }

    private bool HandleDrawMarkBoxes(TeklaDrawingMarkApi markApi, TeklaDrawingDebugOverlayApi debugOverlayApi, string[] args)
    {
        if (!EnsureActiveDrawing())
            return true;

        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var group = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2].Trim() : "mark-boxes";
        var clearFirst = ParseOptionalBoolArg(args, 3, defaultValue: true);

        var markResult = markApi.GetMarks(viewId);
        var request = new DrawingDebugOverlayRequest
        {
            Group = group,
            ClearGroupFirst = clearFirst
        };

        var skippedDegenerate = 0;
        foreach (var mark in markResult.Marks)
        {
            if (mark.ResolvedGeometry?.Corners is not { Count: >= 3 } corners
                || mark.ResolvedGeometry.Width < 0.1
                || mark.ResolvedGeometry.Height < 0.1)
            {
                skippedDegenerate++;
                continue;
            }

            request.Shapes.Add(new DrawingDebugShape
            {
                Kind = "polygon",
                ViewId = mark.ViewId,
                Points = corners.Select(c => new[] { c[0], c[1] }).ToList(),
                Color = "Green",
                LineType = "DashDot"
            });
        }

        var overlayResult = debugOverlayApi.DrawOverlay(JsonSerializer.Serialize(request));
        WriteJson(new
        {
            group = overlayResult.Group,
            viewId,
            totalMarks = markResult.Total,
            drawnMarks = request.Shapes.Count,
            skippedDegenerate,
            clearedCount = overlayResult.ClearedCount,
            createdCount = overlayResult.CreatedCount,
            createdIds = overlayResult.CreatedIds
        });
        return true;
    }

    private bool HandleDrawSelectedMarkTextBoxes(TeklaDrawingDebugOverlayApi debugOverlayApi)
    {
        if (!EnsureActiveDrawing())
            return true;

        var drawingHandler = new DrawingHandler();
        var selectedMarks = new List<Mark>();
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is Mark selectedMark)
                selectedMarks.Add(selectedMark);
        }

        if (selectedMarks.Count != 1)
        {
            WriteError($"Expected exactly 1 selected mark, got {selectedMarks.Count}");
            return true;
        }

        var mark = selectedMarks[0];
        var view = mark.GetView();
        if (view == null)
        {
            WriteError($"Selected mark {mark.GetIdentifier().ID} has no owner view");
            return true;
        }

        var textBoxes = CollectMarkTextBoxes(mark);
        var request = new DrawingDebugOverlayRequest
        {
            Group = "selected-mark-text-boxes",
            ClearGroupFirst = true
        };

        foreach (var textBox in textBoxes)
        {
            if (textBox.Corners.Count < 3 || textBox.Width < 0.1 || textBox.Height < 0.1)
                continue;

            request.Shapes.Add(new DrawingDebugShape
            {
                Kind = "polygon",
                ViewId = view.GetIdentifier().ID,
                Points = textBox.Corners.Select(c => new[] { c[0], c[1] }).ToList(),
                Color = "Magenta",
                LineType = "Solid"
            });
        }

        var overlayResult = debugOverlayApi.DrawOverlay(JsonSerializer.Serialize(request));
        WriteJson(new
        {
            markId = mark.GetIdentifier().ID,
            viewId = view.GetIdentifier().ID,
            placingType = mark.Placing?.GetType().Name ?? "null",
            textBoxCount = textBoxes.Count,
            drawnCount = request.Shapes.Count,
            group = overlayResult.Group,
            clearedCount = overlayResult.ClearedCount,
            createdCount = overlayResult.CreatedCount,
            createdIds = overlayResult.CreatedIds,
            textBoxes = textBoxes.Select(t => new
            {
                source = t.Source,
                objectType = t.ObjectType,
                text = t.Text,
                width = t.Width,
                height = t.Height,
                angleToAxis = t.AngleToAxis,
                centerX = t.CenterX,
                centerY = t.CenterY,
                minX = t.MinX,
                minY = t.MinY,
                maxX = t.MaxX,
                maxY = t.MaxY,
                corners = t.Corners
            })
        });
        return true;
    }

    private bool HandleDrawSelectedMarkObjectAlignedBox(TeklaDrawingDebugOverlayApi debugOverlayApi)
    {
        if (!EnsureActiveDrawing())
            return true;

        var drawingHandler = new DrawingHandler();
        var selectedMarks = new List<Mark>();
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is Mark selectedMark)
                selectedMarks.Add(selectedMark);
        }

        if (selectedMarks.Count != 1)
        {
            WriteError($"Expected exactly 1 selected mark, got {selectedMarks.Count}");
            return true;
        }

        var mark = selectedMarks[0];
        var view = mark.GetView();
        if (view == null)
        {
            WriteError($"Selected mark {mark.GetIdentifier().ID} has no owner view");
            return true;
        }

        var resolved = MarkGeometryHelper.Build(mark, _model, view.GetIdentifier().ID);
        var corners = resolved.Corners
            .Select(c => new[] { Round2(c[0]), Round2(c[1]) })
            .ToList();

        var request = new DrawingDebugOverlayRequest
        {
            Group = "selected-mark-resolved-geometry",
            ClearGroupFirst = true,
            Shapes = new List<DrawingDebugShape>
            {
                new()
                {
                    Kind = "polygon",
                    ViewId = view.GetIdentifier().ID,
                    Points = corners,
                    Color = "Magenta",
                    LineType = "Solid"
                }
            }
        };

        var overlayResult = debugOverlayApi.DrawOverlay(JsonSerializer.Serialize(request));
        WriteJson(new
        {
            markId = mark.GetIdentifier().ID,
            viewId = view.GetIdentifier().ID,
            placingType = mark.Placing?.GetType().Name ?? "null",
            source = resolved.Source,
            isReliable = resolved.IsReliable,
            width = Round2(resolved.Width),
            height = Round2(resolved.Height),
            angleDeg = Round2(resolved.AngleDeg),
            minX = Round2(resolved.MinX),
            minY = Round2(resolved.MinY),
            maxX = Round2(resolved.MaxX),
            maxY = Round2(resolved.MaxY),
            corners,
            group = overlayResult.Group,
            clearedCount = overlayResult.ClearedCount,
            createdCount = overlayResult.CreatedCount,
            createdIds = overlayResult.CreatedIds
        });
        return true;
    }

    private bool HandleDrawSelectedMarkPartAxisGeometry(TeklaDrawingPartGeometryApi partGeometryApi, TeklaDrawingDebugOverlayApi debugOverlayApi)
    {
        if (!EnsureActiveDrawing())
            return true;

        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
        {
            WriteError("No drawing is currently open");
            return true;
        }

        var selectedMarks = new List<Mark>();
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is Mark selectedMark)
                selectedMarks.Add(selectedMark);
        }

        if (selectedMarks.Count != 1)
        {
            WriteError($"Expected exactly 1 selected mark, got {selectedMarks.Count}");
            return true;
        }

        var mark = selectedMarks[0];
        var view = mark.GetView();
        if (view == null)
        {
            WriteError($"Selected mark {mark.GetIdentifier().ID} has no owner view");
            return true;
        }

        int? modelId = null;
        var related = mark.GetRelatedObjects();
        while (related.MoveNext())
        {
            if (related.Current is Tekla.Structures.Drawing.ModelObject drawingModelObject)
            {
                modelId = drawingModelObject.ModelIdentifier.ID;
                break;
            }
        }

        if (!modelId.HasValue)
        {
            WriteError($"Selected mark {mark.GetIdentifier().ID} has no related model object");
            return true;
        }

        var geometry = partGeometryApi.GetPartGeometryInView(view.GetIdentifier().ID, modelId.Value);
        if (!geometry.Success)
        {
            WriteError(geometry.Error ?? "Failed to read part geometry in view");
            return true;
        }

        if (geometry.StartPoint.Length < 2 || geometry.EndPoint.Length < 2)
        {
            WriteError($"Related model object {modelId.Value} does not expose a usable start/end axis in view {view.GetIdentifier().ID}");
            return true;
        }

        var axisDx = geometry.EndPoint[0] - geometry.StartPoint[0];
        var axisDy = geometry.EndPoint[1] - geometry.StartPoint[1];
        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (axisLength < 0.001)
        {
            WriteError($"Related model object {modelId.Value} axis is too short in view {view.GetIdentifier().ID}");
            return true;
        }

        var resolved = MarkGeometryHelper.Build(mark, _model, view.GetIdentifier().ID);
        var centerX = resolved.CenterX;
        var centerY = resolved.CenterY;
        var corners = resolved.Corners
            .Select(c => new[] { Round2(c[0]), Round2(c[1]) })
            .ToList();
        var ux = axisDx / axisLength;
        var uy = axisDy / axisLength;

        var axisHalf = Math.Min(resolved.Width * 0.6, 250.0);
        var axisStart = new[] { Round2(centerX - (ux * axisHalf)), Round2(centerY - (uy * axisHalf)) };
        var axisEnd = new[] { Round2(centerX + (ux * axisHalf)), Round2(centerY + (uy * axisHalf)) };
        var angleDeg = Round2(Math.Atan2(uy, ux) * (180.0 / Math.PI));

        var request = new DrawingDebugOverlayRequest
        {
            Group = "selected-mark-part-axis-geometry",
            ClearGroupFirst = true,
            Shapes = new List<DrawingDebugShape>
            {
                new()
                {
                    Kind = "polygon",
                    ViewId = view.GetIdentifier().ID,
                    Points = corners,
                    Color = "Green",
                    LineType = "DashDot"
                },
                new()
                {
                    Kind = "line",
                    ViewId = view.GetIdentifier().ID,
                    X1 = axisStart[0],
                    Y1 = axisStart[1],
                    X2 = axisEnd[0],
                    Y2 = axisEnd[1],
                    Color = "Cyan",
                    LineType = "Solid"
                },
                new()
                {
                    Kind = "cross",
                    ViewId = view.GetIdentifier().ID,
                    X1 = Round2(centerX),
                    Y1 = Round2(centerY),
                    Size = 30,
                    Color = "Yellow"
                },
                new()
                {
                    Kind = "text",
                    ViewId = view.GetIdentifier().ID,
                    X1 = Round2(centerX + 35),
                    Y1 = Round2(centerY + 25),
                    Text = $"partAxis={angleDeg} deg",
                    Color = "Yellow",
                    TextHeight = 1.5
                }
            }
        };

        var overlayResult = debugOverlayApi.DrawOverlay(JsonSerializer.Serialize(request));
        WriteJson(new
        {
            markId = mark.GetIdentifier().ID,
            viewId = view.GetIdentifier().ID,
            modelId = modelId.Value,
            centerX = Round2(centerX),
            centerY = Round2(centerY),
            source = resolved.Source,
            objectWidth = Round2(resolved.Width),
            objectHeight = Round2(resolved.Height),
            partAxisAngleDeg = angleDeg,
            group = overlayResult.Group,
            clearedCount = overlayResult.ClearedCount,
            createdCount = overlayResult.CreatedCount,
            createdIds = overlayResult.CreatedIds
        });
        return true;
    }

    private bool HandleClearDebugOverlay(TeklaDrawingDebugOverlayApi api, string[] args)
    {
        var group = args.Length >= 2 ? args[1] : string.Empty;
        var result = api.ClearOverlay(group);
        WriteJson(new
        {
            group = result.Group,
            clearedCount = result.ClearedCount
        });
        return true;
    }

    private void WriteGetDrawingPartsResult(GetDrawingPartsResult result)
    {
        WriteJson(new
        {
            total = result.Total,
            parts = result.Parts.Select(p => new
            {
                modelId = p.ModelId,
                type = p.Type,
                partPos = p.PartPos,
                assemblyPos = p.AssemblyPos,
                profile = p.Profile,
                material = p.Material,
                name = p.Name
            })
        });
    }


    private void WritePartInView(PartInView result)
    {
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            modelId = result.ModelId,
            startPoint = result.StartPoint,
            endPoint = result.EndPoint,
            axisX = result.AxisX,
            axisY = result.AxisY,
            bboxMin = result.BboxMin,
            bboxMax = result.BboxMax,
            solidVertices = result.SolidVertices,
            viewHull = result.ViewHull,
            solidGeometryComplete = result.SolidGeometryComplete,
            error = result.Error
        });
    }

    private void WritePartSolidGeometryInViewResult(PartSolidGeometryInViewResult result)
    {
        var solid = result.Solid;
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            modelId = result.ModelId,
            startPoint = result.StartPoint,
            endPoint = result.EndPoint,
            coordinateSystemOrigin = result.CoordinateSystemOrigin,
            axisX = result.AxisX,
            axisY = result.AxisY,
            solid = new
            {
                bboxMin = solid.BboxMin,
                bboxMax = solid.BboxMax,
                vertices = solid.Vertices.Select(vertex => new
                {
                    index = vertex.Index,
                    point = vertex.Point
                }),
                faces = solid.Faces.Select(face => new
                {
                    index = face.Index,
                    normal = face.Normal,
                    loops = face.Loops.Select(loop => new
                    {
                        index = loop.Index,
                        vertexIndexes = loop.VertexIndexes
                    })
                }),
                viewHull = solid.ViewHull,
                solidGeometryComplete = solid.SolidGeometryComplete
            },
            error = result.Error
        });
    }

    private void WritePartCandidatePointsInViewResult(GetPartCandidatePointsResult result)
    {
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            modelId = result.ModelId,
            solidGeometryComplete = result.SolidGeometryComplete,
            candidates = result.Candidates.Select(candidate => new
            {
                modelObjectIds = candidate.ModelObjectIds,
                point = candidate.Point,
                source = candidate.Source.ToString(),
                normal = candidate.Normal,
                inPlaneNormal = candidate.InPlaneNormal,
                confidence = candidate.Confidence.ToString(),
                anchor = new
                {
                    modelObjectId = candidate.Anchor.ModelObjectId,
                    kind = candidate.Anchor.Kind.ToString(),
                    id = candidate.Anchor.Id,
                    key = candidate.Anchor.Key
                },
                reason = new
                {
                    code = candidate.Reason.Code,
                    modelObjectIds = candidate.Reason.ModelObjectIds,
                    values = candidate.Reason.Values
                }
            }),
            error = result.Error
        });
    }

    private void WritePartPointsInViewResult(GetPartPointsResult result)
    {
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            modelId = result.ModelId,
            type = result.Type,
            name = result.Name,
            partPos = result.PartPos,
            profile = result.Profile,
            material = result.Material,
            points = result.Points.Select(p => new
            {
                kind = p.Kind.ToString(),
                sourceKind = p.SourceKind.ToString(),
                index = p.Index,
                point = p.Point
            }),
            error = result.Error
        });
    }

    private void WriteGridAxesResult(GetGridAxesResult result)
    {
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            axes = result.Axes.Select(a => new
            {
                label = a.Label,
                direction = a.Direction,
                coordinate = a.Coordinate,
                startX = a.StartX,
                startY = a.StartY,
                endX = a.EndX,
                endY = a.EndY
            }),
            error = result.Error
        });
    }

    private static List<DebugMarkTextBoxInfo> CollectMarkTextBoxes(Mark mark)
    {
        var results = new List<DebugMarkTextBoxInfo>();
        var visited = new HashSet<int>();
        CollectTextBoxesFromChildren(mark.GetObjects(), "mark.objects", results, visited, depth: 0);
        return results;
    }

    private static void CollectTextBoxesFromChildren(
        DrawingObjectEnumerator? enumerator,
        string source,
        List<DebugMarkTextBoxInfo> results,
        HashSet<int> visited,
        int depth)
    {
        if (enumerator == null || depth > 4)
            return;

        while (enumerator.MoveNext())
            CollectTextBoxesFromObject(enumerator.Current, source, results, visited, depth);
    }

    private static void CollectTextBoxesFromObject(
        object? candidate,
        string source,
        List<DebugMarkTextBoxInfo> results,
        HashSet<int> visited,
        int depth)
    {
        if (candidate == null)
            return;

        var visitId = RuntimeHelpers.GetHashCode(candidate);
        if (!visited.Add(visitId))
            return;

        if (TryCreateDebugTextBox(candidate, source, out var textBox))
            results.Add(textBox);

        if (depth >= 4)
            return;

        try
        {
            var getObjectsMethod = candidate.GetType().GetMethod(
                "GetObjects",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (getObjectsMethod?.Invoke(candidate, null) is DrawingObjectEnumerator childEnumerator)
                CollectTextBoxesFromChildren(childEnumerator, $"{source}>{candidate.GetType().Name}", results, visited, depth + 1);
        }
        catch
        {
            // Ignore objects that do not expose recursive child enumeration.
        }
    }

    private static bool TryCreateDebugTextBox(object candidate, string source, out DebugMarkTextBoxInfo textBox)
    {
        textBox = new DebugMarkTextBoxInfo();

        try
        {
            if (candidate is Text text)
            {
                textBox = CreateDebugTextBox(text.GetObjectAlignedBoundingBox(), source, text.GetType().Name, text.TextString);
                return true;
            }

            var type = candidate.GetType();
            var textProperty = type.GetProperty(
                "TextString",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var looksLikeText = textProperty != null
                || type.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeText)
                return false;

            var objectAlignedMethod = type.GetMethod(
                "GetObjectAlignedBoundingBox",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (objectAlignedMethod?.Invoke(candidate, null) is not RectangleBoundingBox objectAlignedBoundingBox)
                return false;

            textBox = CreateDebugTextBox(
                objectAlignedBoundingBox,
                source,
                type.Name,
                textProperty?.GetValue(candidate, null)?.ToString() ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DebugMarkTextBoxInfo CreateDebugTextBox(
        RectangleBoundingBox box,
        string source,
        string objectType,
        string text)
    {
        return new DebugMarkTextBoxInfo
        {
            Source = source,
            ObjectType = objectType,
            Text = text,
            Width = Round2(box.Width),
            Height = Round2(box.Height),
            AngleToAxis = Round2(box.AngleToAxis),
            CenterX = Round2((box.MinPoint.X + box.MaxPoint.X) / 2.0),
            CenterY = Round2((box.MinPoint.Y + box.MaxPoint.Y) / 2.0),
            MinX = Round2(box.MinPoint.X),
            MinY = Round2(box.MinPoint.Y),
            MaxX = Round2(box.MaxPoint.X),
            MaxY = Round2(box.MaxPoint.Y),
            Corners =
            [
                [Round2(box.LowerLeft.X), Round2(box.LowerLeft.Y)],
                [Round2(box.UpperLeft.X), Round2(box.UpperLeft.Y)],
                [Round2(box.UpperRight.X), Round2(box.UpperRight.Y)],
                [Round2(box.LowerRight.X), Round2(box.LowerRight.Y)]
            ]
        };
    }

    private static double Round2(double value) => Math.Round(value, 2);

    private static bool ParseOptionalBoolArg(string[] args, int index, bool defaultValue)
    {
        if (args.Length <= index || string.IsNullOrWhiteSpace(args[index]))
            return defaultValue;

        var raw = args[index].Trim();
        if (bool.TryParse(raw, out var parsed))
            return parsed;

        return raw switch
        {
            "1" => true,
            "0" => false,
            "yes" => true,
            "no" => false,
            _ => defaultValue
        };
    }

    private sealed class DebugMarkTextBoxInfo
    {
        public string Source { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public double Width { get; set; }
        public double Height { get; set; }
        public double AngleToAxis { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public List<double[]> Corners { get; set; } = [];
    }

}
