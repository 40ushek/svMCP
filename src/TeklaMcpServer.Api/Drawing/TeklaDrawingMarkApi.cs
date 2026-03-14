using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingMarkApi : IDrawingMarkApi
{
    private readonly Model _model;

    public TeklaDrawingMarkApi(Model model) => _model = model;

    public GetMarksResult GetMarks(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        // Performance: disable auto-fetch during enumeration
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {

            var viewsToQuery = viewId.HasValue
                ? new[] { EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                    ?? throw new ViewNotFoundException(viewId.Value) }
                : EnumerateViews(activeDrawing).ToArray();

            var marks   = new List<DrawingMarkInfo>();
            var seenIds = new System.Collections.Generic.HashSet<int>();

            foreach (var view in viewsToQuery)
            {
                var vid        = view.GetIdentifier().ID;
                var markObjects = view.GetAllObjects(typeof(Mark));

                while (markObjects.MoveNext())
                {
                    if (markObjects.Current is not Mark mark) continue;

                    var markId = mark.GetIdentifier().ID;
                    if (!seenIds.Add(markId)) continue; // deduplicate

                    var bbox = mark.GetAxisAlignedBoundingBox();
                    var obb = mark.GetObjectAlignedBoundingBox();
                    var geometry = MarkGeometryHelper.Build(mark, _model, vid);
                    var ins  = mark.InsertionPoint;
                    var leaderLinePlacing = mark.Placing as LeaderLinePlacing;
                    var info = new DrawingMarkInfo
                    {
                        Id         = markId,
                        ViewId     = vid,
                        InsertionX = Math.Round(ins.X, 1),
                        InsertionY = Math.Round(ins.Y, 1),
                        BboxMinX   = Math.Round(bbox.MinPoint.X, 1),
                        BboxMinY   = Math.Round(bbox.MinPoint.Y, 1),
                        BboxMaxX    = Math.Round(bbox.MaxPoint.X, 1),
                        BboxMaxY    = Math.Round(bbox.MaxPoint.Y, 1),
                        CenterX    = Math.Round((bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0, 2),
                        CenterY    = Math.Round((bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0, 2),
                        PlacingType = mark.Placing?.GetType().Name ?? "null",
                        PlacingX    = leaderLinePlacing != null ? Math.Round(leaderLinePlacing.StartPoint.X, 2) : 0,
                        PlacingY    = leaderLinePlacing != null ? Math.Round(leaderLinePlacing.StartPoint.Y, 2) : 0,
                        Angle      = Math.Round(mark.Attributes.Angle, 2),
                        RotationAngle = Math.Round(mark.Attributes.RotationAngle, 2),
                        TextAlignment = mark.Attributes.TextAlignment.ToString(),
                        ObjectAlignedBoundingBox = new MarkObjectAlignedBoundingBoxInfo
                        {
                            Width = Math.Round(obb.Width, 2),
                            Height = Math.Round(obb.Height, 2),
                            AngleToAxis = Math.Round(obb.AngleToAxis, 2),
                            CenterX = Math.Round((obb.MinPoint.X + obb.MaxPoint.X) / 2.0, 2),
                            CenterY = Math.Round((obb.MinPoint.Y + obb.MaxPoint.Y) / 2.0, 2),
                            MinX = Math.Round(obb.MinPoint.X, 2),
                            MinY = Math.Round(obb.MinPoint.Y, 2),
                            MaxX = Math.Round(obb.MaxPoint.X, 2),
                            MaxY = Math.Round(obb.MaxPoint.Y, 2),
                            Corners = new List<double[]>
                            {
                                new[] { Math.Round(obb.LowerLeft.X, 2), Math.Round(obb.LowerLeft.Y, 2) },
                                new[] { Math.Round(obb.UpperLeft.X, 2), Math.Round(obb.UpperLeft.Y, 2) },
                                new[] { Math.Round(obb.UpperRight.X, 2), Math.Round(obb.UpperRight.Y, 2) },
                                new[] { Math.Round(obb.LowerRight.X, 2), Math.Round(obb.LowerRight.Y, 2) }
                            }
                        },
                        ResolvedGeometry = new MarkResolvedGeometryInfo
                        {
                            Source = geometry.Source,
                            IsReliable = geometry.IsReliable,
                            Width = Math.Round(geometry.Width, 2),
                            Height = Math.Round(geometry.Height, 2),
                            CenterX = Math.Round(geometry.CenterX, 2),
                            CenterY = Math.Round(geometry.CenterY, 2),
                            MinX = Math.Round(geometry.MinX, 2),
                            MinY = Math.Round(geometry.MinY, 2),
                            MaxX = Math.Round(geometry.MaxX, 2),
                            MaxY = Math.Round(geometry.MaxY, 2),
                            AngleDeg = Math.Round(geometry.AngleDeg, 2),
                            Corners = geometry.Corners
                                .Select(c => new[] { Math.Round(c[0], 2), Math.Round(c[1], 2) })
                                .ToList()
                        },
                        ArrowHead   = new MarkArrowheadInfo
                        {
                            Type = mark.Attributes.ArrowHead.Head.ToString(),
                            Position = mark.Attributes.ArrowHead.ArrowPosition.ToString(),
                            Height = Math.Round(mark.Attributes.ArrowHead.Height, 2),
                            Width = Math.Round(mark.Attributes.ArrowHead.Width, 2)
                        }
                    };

                    if (mark.Placing is BaseLinePlacing baseLinePlacing)
                    {
                        var axisDx = baseLinePlacing.EndPoint.X - baseLinePlacing.StartPoint.X;
                        var axisDy = baseLinePlacing.EndPoint.Y - baseLinePlacing.StartPoint.Y;
                        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
                        var normalizedDx = axisLength >= 0.001 ? axisDx / axisLength : 0.0;
                        var normalizedDy = axisLength >= 0.001 ? axisDy / axisLength : 0.0;

                        info.Axis = new MarkAxisInfo
                        {
                            StartX = Math.Round(baseLinePlacing.StartPoint.X, 2),
                            StartY = Math.Round(baseLinePlacing.StartPoint.Y, 2),
                            EndX = Math.Round(baseLinePlacing.EndPoint.X, 2),
                            EndY = Math.Round(baseLinePlacing.EndPoint.Y, 2),
                            Dx = Math.Round(normalizedDx, 4),
                            Dy = Math.Round(normalizedDy, 4),
                            Length = Math.Round(axisLength, 2),
                            AngleDeg = Math.Round(Math.Atan2(axisDy, axisDx) * (180.0 / Math.PI), 2),
                            IsReliable = axisLength >= 0.001
                        };
                    }

                    // Resolve model object ID from first related drawing object
                    var related = mark.GetRelatedObjects();
                    while (related.MoveNext())
                    {
                        if (related.Current is Tekla.Structures.Drawing.ModelObject mo)
                        {
                            info.ModelId = mo.ModelIdentifier.ID;
                            break;
                        }
                    }

                    // Read property element names and their computed values
                    var contentEnum = mark.Attributes.Content.GetEnumerator();
                    while (contentEnum.MoveNext())
                    {
                        if (contentEnum.Current is PropertyElement prop)
                            info.Properties.Add(new MarkPropertyValue { Name = prop.Name, Value = prop.Value });
                    }

                    var children = mark.GetObjects();
                    while (children.MoveNext())
                    {
                        if (children.Current is not LeaderLine leaderLine)
                            continue;

                        var leaderInfo = new MarkLeaderLineInfo
                        {
                            Type = leaderLine.LeaderLineType.ToString(),
                            StartX = Math.Round(leaderLine.StartPoint.X, 2),
                            StartY = Math.Round(leaderLine.StartPoint.Y, 2),
                            EndX = Math.Round(leaderLine.EndPoint.X, 2),
                            EndY = Math.Round(leaderLine.EndPoint.Y, 2)
                        };

                        foreach (Point elbowPoint in leaderLine.ElbowPoints)
                        {
                            leaderInfo.ElbowPoints.Add(new[]
                            {
                                Math.Round(elbowPoint.X, 2),
                                Math.Round(elbowPoint.Y, 2)
                            });
                        }

                        info.LeaderLines.Add(leaderInfo);
                    }

                    marks.Add(info);
                }
            }

            // Detect overlaps using the actual resolved mark polygons when available.
            var overlaps = new List<MarkOverlap>();
            for (int i = 0; i < marks.Count; i++)
            for (int j = i + 1; j < marks.Count; j++)
            {
                var a = marks[i]; var b = marks[j];
                var overlapsDetected =
                    a.ResolvedGeometry?.Corners is { Count: >= 3 } aCorners &&
                    b.ResolvedGeometry?.Corners is { Count: >= 3 } bCorners
                        ? MarkGeometryHelper.PolygonsIntersect(aCorners, bCorners)
                        : MarkGeometryHelper.RectanglesOverlap(
                            a.ResolvedGeometry?.MinX ?? a.BboxMinX,
                            a.ResolvedGeometry?.MinY ?? a.BboxMinY,
                            a.ResolvedGeometry?.MaxX ?? a.BboxMaxX,
                            a.ResolvedGeometry?.MaxY ?? a.BboxMaxY,
                            b.ResolvedGeometry?.MinX ?? b.BboxMinX,
                            b.ResolvedGeometry?.MinY ?? b.BboxMinY,
                            b.ResolvedGeometry?.MaxX ?? b.BboxMaxX,
                            b.ResolvedGeometry?.MaxY ?? b.BboxMaxY);

                if (overlapsDetected)
                    overlaps.Add(new MarkOverlap { IdA = a.Id, IdB = b.Id });
            }

            return new GetMarksResult { Total = marks.Count, Marks = marks, Overlaps = overlaps };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public ResolveMarksResult ResolveMarkOverlaps(double margin)
    {
        var total = Stopwatch.StartNew();
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var movedIds = new List<int>();
            var totalIterations = 0;
            var totalRemainingOverlaps = 0;

            foreach (var view in EnumerateViews(activeDrawing))
            {
                var viewTotal = Stopwatch.StartNew();
                var collect = Stopwatch.StartNew();
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, _model);
                collect.Stop();

                if (markEntries.Count == 0)
                {
                    PerfTrace.Write("api-mark", "resolve_mark_overlaps_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks=0 collectMs={collect.ElapsedMilliseconds}");
                    continue;
                }

                var placements = markEntries.Select(e => new MarkLayoutPlacement
                {
                    Id = e.Mark.GetIdentifier().ID,
                    X = e.CenterX,
                    Y = e.CenterY,
                    Width = e.Item.Width,
                    Height = e.Item.Height,
                    AnchorX = e.Item.AnchorX,
                    AnchorY = e.Item.AnchorY,
                    HasLeaderLine = e.Item.HasLeaderLine,
                    HasAxis = e.Item.HasAxis,
                    AxisDx = e.Item.AxisDx,
                    AxisDy = e.Item.AxisDy,
                    CanMove = true,
                    LocalCorners = e.Item.LocalCorners.Select(c => new[] { c[0], c[1] }).ToList()
                }).ToList();

                var resolver = new MarkOverlapResolver();
                var resolve = Stopwatch.StartNew();
                var resolved = resolver.ResolvePlacedMarks(
                    placements,
                    new MarkLayoutOptions
                    {
                        Gap = margin,
                        MaxResolverIterations = 24,
                        // Keep marks close to their object anchor in post-fix mode.
                        MaxDistanceFromAnchor = 140.0
                    },
                    out var iterations);
                resolve.Stop();
                var resolvedById = resolved.ToDictionary(x => x.Id);

                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, resolvedById));
                apply.Stop();
                totalIterations += iterations;
                
                var finalViewOverlaps = GetMarks(view.GetIdentifier().ID).Overlaps.Count;
                totalRemainingOverlaps += finalViewOverlaps;
                PerfTrace.Write("api-mark", "resolve_mark_overlaps_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} resolveMs={resolve.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} iterations={iterations} finalOverlaps={finalViewOverlaps}");
            }

            movedIds = movedIds.Distinct().ToList();

            if (movedIds.Count > 0)
                activeDrawing.CommitChanges();

            return new ResolveMarksResult
            {
                MarksMovedCount = movedIds.Count,
                MovedIds = movedIds,
                Iterations = totalIterations,
                RemainingOverlaps = totalRemainingOverlaps
            };
        }
        finally
        {
            PerfTrace.Write("api-mark", "resolve_mark_overlaps_total", total.ElapsedMilliseconds, $"margin={margin.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private List<int> NudgeOverlapsInView(View view, double margin)
    {
        var viewId = view.GetIdentifier().ID;
        var snapshot = GetMarks(viewId);
        if (snapshot.Overlaps.Count == 0)
            return new List<int>();

        var marksById = new Dictionary<int, Mark>();
        var markEnum = view.GetAllObjects(typeof(Mark));
        while (markEnum.MoveNext())
        {
            if (markEnum.Current is Mark mark)
                marksById[mark.GetIdentifier().ID] = mark;
        }

        var attemptsById = new Dictionary<int, int>();
        var moved = new HashSet<int>();

        foreach (var overlap in snapshot.Overlaps)
        {
            if (TryNudgeOne(overlap.IdB))
                continue;

            _ = TryNudgeOne(overlap.IdA);
        }

        return moved.ToList();

        bool TryNudgeOne(int markId)
        {
            if (!marksById.TryGetValue(markId, out var mark))
                return false;

            var markInfo = snapshot.Marks.FirstOrDefault(x => x.Id == markId);
            if (markInfo == null)
                return false;

            var attempt = attemptsById.TryGetValue(markId, out var count) ? count : 0;
            attemptsById[markId] = attempt + 1;

            var width = Math.Max(markInfo.BboxMaxX - markInfo.BboxMinX, 1.0);
            var step = Math.Max(width + margin, 40.0) * (1.0 + ((attempt / 2) * 0.6));
            var direction = attempt % 2 == 0 ? 1.0 : -1.0;

            var moveX = 1.0;
            var moveY = 0.0;
            if (markInfo.Axis?.IsReliable == true)
            {
                moveX = -markInfo.Axis.Dy;
                moveY = markInfo.Axis.Dx;
                var length = Math.Sqrt((moveX * moveX) + (moveY * moveY));
                if (length >= 0.001)
                {
                    moveX /= length;
                    moveY /= length;
                }
            }

            var before = mark.InsertionPoint;
            mark.InsertionPoint = new Point(
                before.X + (moveX * step * direction),
                before.Y + (moveY * step * direction),
                before.Z);

            if (!mark.Modify())
                return false;

            var after = mark.InsertionPoint;
            if (Math.Abs(after.X - before.X) < 0.05 && Math.Abs(after.Y - before.Y) < 0.05)
                return false;

            moved.Add(markId);
            return true;
        }
    }


    public ResolveMarksResult ArrangeMarks(double gap)
    {
        var total = Stopwatch.StartNew();
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var engine = new MarkLayoutEngine();
            var movedIds = new List<int>();
            var totalIterations = 0;
            var totalRemainingOverlaps = 0;

            foreach (var view in EnumerateViews(activeDrawing))
            {
                var viewTotal = Stopwatch.StartNew();
                var collect = Stopwatch.StartNew();
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, _model);
                collect.Stop();
                if (markEntries.Count == 0)
                {
                    PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks=0 collectMs={collect.ElapsedMilliseconds}");
                    continue;
                }

                var arrange = Stopwatch.StartNew();
                var layoutResult = engine.Arrange(
                    markEntries.Select(x => x.Item),
                    new MarkLayoutOptions
                    {
                        Gap = gap,
                        CurrentPositionWeight = 1.2,
                        AnchorDistanceWeight = 2.5,
                        MaxDistanceFromAnchor = 140.0,
                        CandidateDistanceMultipliers = new[] { 1.0, 1.25, 2.0 },
                        LeaderLengthWeight = 15.0,
                    });
                arrange.Stop();

                var placementById = layoutResult.Placements.ToDictionary(x => x.Id);
                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById));
                apply.Stop();
                totalIterations += layoutResult.Iterations;
                totalRemainingOverlaps += layoutResult.RemainingOverlaps;
                PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} arrangeMs={arrange.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} iterations={layoutResult.Iterations}");
            }

            if (movedIds.Count > 0)
                activeDrawing.CommitChanges();

            return new ResolveMarksResult
            {
                MarksMovedCount   = movedIds.Count,
                MovedIds          = movedIds,
                Iterations        = totalIterations,
                RemainingOverlaps = totalRemainingOverlaps
            };
        }
        finally
        {
            PerfTrace.Write("api-mark", "arrange_marks_total", total.ElapsedMilliseconds, $"gap={gap.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public CreateMarksResult CreatePartMarks(string contentAttributesCsv, string markAttributesFile, string frameType, string arrowheadType)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var contentAttrs = (contentAttributesCsv ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();

        // Track whether caller explicitly requested content elements.
        // If not, the attributes file defines content — don't override.
        bool contentExplicit = contentAttrs.Count > 0;
        if (!contentExplicit && string.IsNullOrWhiteSpace(markAttributesFile))
            contentAttrs.Add("ASSEMBLY_POS"); // fallback when no file either

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var createdIds = new List<int>();
            var skipped = 0;
            bool? attributesLoaded = null;

            foreach (var view in EnumerateViews(activeDrawing))
            {
                // A part can legitimately appear in multiple views and may need
                // a separate drawing mark in each one. Deduplicate only inside
                // the current view to avoid duplicate enumerator entries.
                var seenModelIds = new HashSet<int>();
                var partEnum = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
                while (partEnum.MoveNext())
                {
                    var part = partEnum.Current as Tekla.Structures.Drawing.Part;
                    if (part == null) continue;

                    var modelId = part.ModelIdentifier.ID;
                    if (!seenModelIds.Add(modelId)) { skipped++; continue; }

                    // Place at view origin — user runs arrange_marks to distribute around anchors
                    var mark = new Mark(part);
                    mark.Placing = new LeaderLinePlacing(new Point(0, 0, 0));

                    // Set content before Insert only when explicitly requested.
                    if (contentExplicit)
                    {
                        var content = mark.Attributes.Content;
                        content.Clear();
                        foreach (var attr in contentAttrs)
                        {
                            var element = CreatePropertyElement(attr);
                            if (element != null) content.Add(element);
                        }
                    }

                    if (mark.Insert())
                    {
                        // After Insert, Tekla resolves the part's actual position into
                        // LeaderLinePlacing.StartPoint. Now load visual attributes from
                        // file (frame, leader line style, font, color, content…).
                        if (!string.IsNullOrWhiteSpace(markAttributesFile))
                        {
                            bool loaded = mark.Attributes.LoadAttributes(markAttributesFile);
                            if (attributesLoaded == null) attributesLoaded = loaded;
                        }

                        // Re-apply explicit content override after LoadAttributes if provided
                        // (LoadAttributes would have reset it to file defaults).
                        if (contentExplicit)
                        {
                            var content2 = mark.Attributes.Content;
                            content2.Clear();
                            foreach (var attr in contentAttrs)
                            {
                                var element = CreatePropertyElement(attr);
                                if (element != null) content2.Add(element);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(frameType) &&
                            Enum.TryParse<FrameTypes>(frameType, ignoreCase: true, out var parsedFrame))
                            mark.Attributes.Frame.Type = parsedFrame;

                        if (!string.IsNullOrWhiteSpace(arrowheadType) &&
                            Enum.TryParse<ArrowheadTypes>(arrowheadType, ignoreCase: true, out var parsedArrow))
                            mark.Attributes.ArrowHead.Head = parsedArrow;

                        if (mark.Placing is LeaderLinePlacing lp)
                            mark.InsertionPoint = new Point(lp.StartPoint.X, lp.StartPoint.Y, 0);

                        mark.Modify();
                        createdIds.Add(mark.GetIdentifier().ID);
                    }
                    else
                        skipped++;
                }
            }

            if (createdIds.Count > 0)
                activeDrawing.CommitChanges("(MCP) CreatePartMarks");

            return new CreateMarksResult
            {
                CreatedCount     = createdIds.Count,
                SkippedCount     = skipped,
                CreatedMarkIds   = createdIds,
                AttributesLoaded = attributesLoaded
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public DeleteAllMarksResult DeleteAllMarks()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var deletedCount = 0;
        var viewEnum = activeDrawing.GetSheet().GetViews();
        while (viewEnum.MoveNext())
        {
            if (viewEnum.Current is not View view)
                continue;

            var markEnum = view.GetAllObjects(typeof(Mark));
            while (markEnum.MoveNext())
            {
                if (markEnum.Current is not Mark mark)
                    continue;

                mark.Delete();
                deletedCount++;
            }
        }

        if (deletedCount > 0)
            activeDrawing.CommitChanges("(MCP) DeleteAllMarks");

        return new DeleteAllMarksResult
        {
            DeletedCount = deletedCount
        };
    }

    public SetMarkContentResult SetMarkContent(SetMarkContentRequest request)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var result = new SetMarkContentResult();
        var targetIds = new HashSet<int>(request.TargetIds ?? Array.Empty<int>());
        var requestedContentElements = request.RequestedContentElements ?? Array.Empty<string>();
        if (!Enum.IsDefined(typeof(DrawingColors), request.FontColorValue))
        {
            return new SetMarkContentResult
            {
                Errors = { $"Invalid FontColorValue: {request.FontColorValue}" }
            };
        }
        var parsedColor = (DrawingColors)request.FontColorValue;

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
            while (drawingObjects.MoveNext())
            {
                if (drawingObjects.Current is not Mark mark)
                    continue;

                var drawingId = mark.GetIdentifier().ID;
                var matches = targetIds.Contains(drawingId);

                if (!matches)
                {
                    var related = mark.GetRelatedObjects();
                    while (related.MoveNext())
                    {
                        if (related.Current is Tekla.Structures.Drawing.ModelObject relatedModelObject &&
                            targetIds.Contains(relatedModelObject.ModelIdentifier.ID))
                        {
                            matches = true;
                            break;
                        }
                    }
                }

                if (!matches)
                    continue;

                try
                {
                    var content = mark.Attributes.Content;
                    var existingFont = default(FontAttributes);
                    var contentEnumerator = content.GetEnumerator();
                    if (contentEnumerator.MoveNext() && contentEnumerator.Current is PropertyElement existingProperty && existingProperty.Font != null)
                        existingFont = (FontAttributes)existingProperty.Font.Clone();

                    var newFont = existingFont != null ? (FontAttributes)existingFont.Clone() : new FontAttributes();
                    var fontChanged = false;

                    if (request.UpdateFontName && !string.Equals(newFont.Name, request.FontName, StringComparison.Ordinal))
                    {
                        newFont.Name = request.FontName;
                        fontChanged = true;
                    }

                    if (request.UpdateFontHeight && Math.Abs(newFont.Height - request.FontHeight) > 0.01)
                    {
                        newFont.Height = request.FontHeight;
                        fontChanged = true;
                    }

                    if (request.UpdateFontColor && newFont.Color != parsedColor)
                    {
                        newFont.Color = parsedColor;
                        fontChanged = true;
                    }

                    if (request.UpdateContent)
                    {
                        content.Clear();
                        foreach (var attribute in requestedContentElements)
                        {
                            var element = CreateSetMarkContentPropertyElement(attribute);
                            if (element == null)
                            {
                                result.Errors.Add($"Object {drawingId}: unsupported content attribute '{attribute}'.");
                                continue;
                            }

                            element.Font = (FontAttributes)newFont.Clone();
                            content.Add(element);
                        }
                    }
                    else if (fontChanged)
                    {
                        var existingElements = content.GetEnumerator();
                        while (existingElements.MoveNext())
                        {
                            if (existingElements.Current is PropertyElement propElement)
                                propElement.Font = (FontAttributes)newFont.Clone();
                        }
                    }

                    if (mark.Modify())
                        result.UpdatedObjectIds.Add(drawingId);
                    else
                        result.FailedObjectIds.Add(drawingId);
                }
                catch (Exception markEx)
                {
                    result.FailedObjectIds.Add(drawingId);
                    result.Errors.Add($"Object {drawingId}: {markEx.Message}");
                }
            }

            if (result.UpdatedObjectIds.Count > 0)
                activeDrawing.CommitChanges("(MCP) SetMarkContent");

            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private static PropertyElement? CreatePropertyElement(string attributeName) =>
        (attributeName ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PART_POS" or "PARTPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition()),
            "PROFILE" or "PART_PROFILE"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile()),
            "MATERIAL" or "PART_MATERIAL"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material()),
            "ASSEMBLY_POS" or "ASSEMBLYPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition()),
            "NAME"   => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name()),
            "CLASS"  => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class()),
            "SIZE"   => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size()),
            "CAMBER" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber()),
            _        => null
        };

    private static PropertyElement? CreateSetMarkContentPropertyElement(string attributeName) =>
        (attributeName ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PART_POS" or "PARTPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition()),
            "PROFILE" or "PART_PROFILE"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile()),
            "MATERIAL" or "PART_MATERIAL"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material()),
            "ASSEMBLY_POS" or "PART_PREFIX" or "ASSEMBLYPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition()),
            "NAME"     => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name()),
            "CLASS"    => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class()),
            "SIZE"     => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size()),
            "CAMBER"   => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber()),
            _          => null
        };

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
