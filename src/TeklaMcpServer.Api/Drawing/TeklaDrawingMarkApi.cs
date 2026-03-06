using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Marks;

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
                    var ins  = mark.InsertionPoint;
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
                        PlacingType = mark.Placing?.GetType().Name ?? "null",
                        PlacingX    = mark.Placing is LeaderLinePlacing lp2 ? Math.Round(lp2.StartPoint.X, 2) : 0,
                        PlacingY    = mark.Placing is LeaderLinePlacing lp3 ? Math.Round(lp3.StartPoint.Y, 2) : 0
                    };

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

                    marks.Add(info);
                }
            }

            // Detect pairwise AABB overlaps
            var overlaps = new List<MarkOverlap>();
            for (int i = 0; i < marks.Count; i++)
            for (int j = i + 1; j < marks.Count; j++)
            {
                var a = marks[i]; var b = marks[j];
                if (a.BboxMaxX > b.BboxMinX && b.BboxMaxX > a.BboxMinX &&
                    a.BboxMaxY > b.BboxMinY && b.BboxMaxY > a.BboxMinY)
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
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view);
                if (markEntries.Count == 0)
                    continue;

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
                    CanMove = true
                }).ToList();

                var resolver = new MarkOverlapResolver();
                var resolved = resolver.Resolve(placements, new MarkLayoutOptions { Gap = margin }, out var iterations);
                var resolvedById = resolved.ToDictionary(x => x.Id);

                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, resolvedById));
                totalIterations += iterations;
                totalRemainingOverlaps += resolver.CountOverlaps(resolved);
            }

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
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public ResolveMarksResult ArrangeMarks(double gap)
    {
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
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view);
                if (markEntries.Count == 0)
                    continue;

                var layoutResult = engine.Arrange(
                    markEntries.Select(x => x.Item),
                    new MarkLayoutOptions
                    {
                        Gap = gap,
                        CurrentPositionWeight = 0.3,
                        LeaderLengthWeight = 15.0,
                    });

                var placementById = layoutResult.Placements.ToDictionary(x => x.Id);
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById));
                totalIterations += layoutResult.Iterations;
                totalRemainingOverlaps += layoutResult.RemainingOverlaps;
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

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
