using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingMarkApi
{
    public MoveMarkResult MoveMark(int markId, double insertionX, double insertionY)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            Mark? targetMark = null;
            var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
            while (drawingObjects.MoveNext())
            {
                if (drawingObjects.Current is not Mark mark)
                    continue;

                if (mark.GetIdentifier().ID != markId)
                    continue;

                targetMark = mark;
                break;
            }

            if (targetMark == null)
                throw new Exception($"Mark {markId} not found");

            targetMark.InsertionPoint = new Point(insertionX, insertionY, 0);
            if (!targetMark.Modify())
                throw new Exception($"Mark {markId} modify failed");

            activeDrawing.CommitChanges("(MCP) MoveMark");

            return new MoveMarkResult
            {
                Moved = true,
                MarkId = markId,
                InsertionX = insertionX,
                InsertionY = insertionY
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

        bool contentExplicit = contentAttrs.Count > 0;
        if (!contentExplicit && string.IsNullOrWhiteSpace(markAttributesFile))
            contentAttrs.Add("ASSEMBLY_POS");

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var createdIds = new List<int>();
            var skipped = 0;
            bool? attributesLoaded = null;

            foreach (var view in EnumerateViews(activeDrawing))
            {
                var seenModelIds = new HashSet<int>();
                var partEnum = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
                while (partEnum.MoveNext())
                {
                    var part = partEnum.Current as Tekla.Structures.Drawing.Part;
                    if (part == null)
                        continue;

                    var modelId = part.ModelIdentifier.ID;
                    if (!seenModelIds.Add(modelId))
                    {
                        skipped++;
                        continue;
                    }

                    var mark = new Mark(part);
                    mark.Placing = new LeaderLinePlacing(new Point(0, 0, 0));

                    if (contentExplicit)
                    {
                        var content = mark.Attributes.Content;
                        content.Clear();
                        foreach (var attr in contentAttrs)
                        {
                            var element = CreatePropertyElement(attr);
                            if (element != null)
                                content.Add(element);
                        }
                    }

                    if (mark.Insert())
                    {
                        if (!string.IsNullOrWhiteSpace(markAttributesFile))
                        {
                            var loaded = mark.Attributes.LoadAttributes(markAttributesFile);
                            if (attributesLoaded == null)
                                attributesLoaded = loaded;
                        }

                        if (contentExplicit)
                        {
                            var content2 = mark.Attributes.Content;
                            content2.Clear();
                            foreach (var attr in contentAttrs)
                            {
                                var element = CreatePropertyElement(attr);
                                if (element != null)
                                    content2.Add(element);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(frameType) &&
                            Enum.TryParse<FrameTypes>(frameType, ignoreCase: true, out var parsedFrame))
                        {
                            mark.Attributes.Frame.Type = parsedFrame;
                        }

                        if (!string.IsNullOrWhiteSpace(arrowheadType) &&
                            Enum.TryParse<ArrowheadTypes>(arrowheadType, ignoreCase: true, out var parsedArrow))
                        {
                            mark.Attributes.ArrowHead.Head = parsedArrow;
                        }

                        if (mark.Placing is LeaderLinePlacing lp)
                            mark.InsertionPoint = new Point(lp.StartPoint.X, lp.StartPoint.Y, 0);

                        mark.Modify();
                        createdIds.Add(mark.GetIdentifier().ID);
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            if (createdIds.Count > 0)
                activeDrawing.CommitChanges("(MCP) CreatePartMarks");

            return new CreateMarksResult
            {
                CreatedCount = createdIds.Count,
                SkippedCount = skipped,
                CreatedMarkIds = createdIds,
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
}
