using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingInteractionApi : IDrawingInteractionApi
{
    private readonly Model _model;

    public TeklaDrawingInteractionApi(Model model)
    {
        _model = model;
    }

    public SelectDrawingObjectsResult SelectObjectsByModelIds(IReadOnlyCollection<int> targetModelIds)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetSet = targetModelIds?.ToHashSet() ?? new HashSet<int>();
        var drawingObjectsToSelect = new ArrayList();
        var result = new SelectDrawingObjectsResult();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var allDrawingObjects = activeDrawing.GetSheet().GetAllObjects();
            while (allDrawingObjects.MoveNext())
            {
                if (allDrawingObjects.Current is not Tekla.Structures.Drawing.ModelObject drawingModelObject)
                    continue;
                if (!targetSet.Contains(drawingModelObject.ModelIdentifier.ID))
                    continue;

                drawingObjectsToSelect.Add(drawingModelObject);
                result.SelectedDrawingObjectIds.Add(drawingModelObject.GetIdentifier().ID);
                result.SelectedModelIds.Add(drawingModelObject.ModelIdentifier.ID);
            }

            if (drawingObjectsToSelect.Count == 0)
                return result;

            var drawingHandler = new DrawingHandler();
            var selector = drawingHandler.GetDrawingObjectSelector();
            selector.SelectObjects(drawingObjectsToSelect, false);
            activeDrawing.CommitChanges("(MCP) SelectDrawingObjects");
            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public FilterDrawingObjectsResult FilterObjects(string objectType, string specificType)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetType = ResolveDrawingType(objectType);
        if (targetType == null)
            return new FilterDrawingObjectsResult { IsKnownType = false };

        var result = new FilterDrawingObjectsResult { IsKnownType = true };
        var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
        while (drawingObjects.MoveNext())
        {
            if (drawingObjects.Current is not DrawingObject drawingObject)
                continue;
            if (!targetType.IsInstanceOfType(drawingObject))
                continue;

            if (drawingObject is Mark mark && !string.IsNullOrWhiteSpace(specificType))
            {
                var markType = GetMarkType(mark);
                if (!string.Equals(markType, specificType, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            result.Objects.Add(new DrawingObjectItem
            {
                Id = drawingObject.GetIdentifier().ID,
                Type = drawingObject.GetType().Name,
                ModelId = drawingObject is Tekla.Structures.Drawing.ModelObject dm ? dm.ModelIdentifier.ID : (int?)null
            });
        }

        return result;
    }

    public DrawingContextResult GetDrawingContext()
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var result = new DrawingContextResult
        {
            Drawing = new DrawingContextDrawingInfo
            {
                Guid = activeDrawing.GetIdentifier().GUID.ToString(),
                Name = activeDrawing.Name ?? string.Empty,
                Mark = activeDrawing.Mark ?? string.Empty,
                Type = activeDrawing.GetType().Name,
                Status = activeDrawing.UpToDateStatus.ToString()
            }
        };

        var selector = drawingHandler.GetDrawingObjectSelector();
        var selected = selector.GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is not DrawingObject selectedObject)
                continue;

            result.SelectedObjects.Add(new DrawingObjectItem
            {
                Id = selectedObject.GetIdentifier().ID,
                Type = selectedObject.GetType().Name,
                ModelId = selectedObject is Tekla.Structures.Drawing.ModelObject dm ? dm.ModelIdentifier.ID : (int?)null
            });
        }

        return result;
    }

    public SheetObjectsDebugResult GetSheetObjectsDebug()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        double sheetWidth = 0;
        double sheetHeight = 0;
        try
        {
            var size = activeDrawing.Layout.SheetSize;
            sheetWidth = size.Width;
            sheetHeight = size.Height;
        }
        catch
        {
        }

        var result = new SheetObjectsDebugResult
        {
            Drawing = new DrawingContextDrawingInfo
            {
                Guid = activeDrawing.GetIdentifier().GUID.ToString(),
                Name = activeDrawing.Name ?? string.Empty,
                Mark = activeDrawing.Mark ?? string.Empty,
                Type = activeDrawing.GetType().Name,
                Status = activeDrawing.UpToDateStatus.ToString()
            },
            SheetWidth = sheetWidth,
            SheetHeight = sheetHeight
        };

        var sheet = activeDrawing.GetSheet();
        var sheetId = sheet.GetIdentifier().ID;
        var objects = sheet.GetAllObjects();
        while (objects.MoveNext())
        {
            result.TotalObjectsScanned++;
            if (objects.Current is not DrawingObject drawingObject)
                continue;

            var ownerView = drawingObject.GetView();
            var isSheetLevel = ownerView != null && ownerView.GetIdentifier().ID == sheetId;

            // Collect ALL object types for discovery (not just sheet-level)
            var item = new SheetObjectDebugItem
            {
                Id = drawingObject.GetIdentifier().ID,
                Type = drawingObject.GetType().Name,
                ModelId = drawingObject is Tekla.Structures.Drawing.ModelObject drawingModelObject2
                    ? drawingModelObject2.ModelIdentifier.ID
                    : (int?)null,
                IsSheetLevel = isSheetLevel,
                OwnerViewType = ownerView?.GetType().Name ?? string.Empty,
                OwnerViewName = ownerView is View ownerNamedView2 ? ownerNamedView2.Name ?? string.Empty : string.Empty
            };

            if (drawingObject is IAxisAlignedBoundingBox bounded2)
            {
                var box2 = bounded2.GetAxisAlignedBoundingBox();
                if (box2 != null)
                {
                    item.HasBoundingBox = true;
                    item.BboxMinX = box2.MinPoint.X;
                    item.BboxMinY = box2.MinPoint.Y;
                    item.BboxMaxX = box2.MaxPoint.X;
                    item.BboxMaxY = box2.MaxPoint.Y;
                }
            }

            result.SheetLevelObjects.Add(item);
        }

        result.ReservedAreaCandidates.AddRange(DrawingReservedAreaReader.Read(activeDrawing, 0, 0));

        return result;
    }

    private static Type? ResolveDrawingType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return null;

        return Type.GetType($"Tekla.Structures.Drawing.{objectType}, Tekla.Structures.Drawing", false, true);
    }

    private string GetMarkType(Mark mark)
    {
        var associatedObjects = mark.GetRelatedObjects();
        foreach (object associated in associatedObjects)
        {
            if (associated is not Tekla.Structures.Drawing.ModelObject drawingModelObject)
                continue;

            var modelObject = _model.SelectModelObject(drawingModelObject.ModelIdentifier);
            if (modelObject == null) continue;
            if (modelObject is Tekla.Structures.Model.Part) return "Part Mark";
            if (modelObject is BoltGroup) return "Bolt Mark";
            if (modelObject is RebarGroup || modelObject is SingleRebar) return "Reinforcement Mark";
            if (modelObject is Tekla.Structures.Model.Weld) return "Weld Mark";
            if (modelObject is Assembly) return "Assembly Mark";
            if (modelObject is Tekla.Structures.Model.Connection) return "Connection Mark";
        }

        return "Unknown Mark Type";
    }
}
