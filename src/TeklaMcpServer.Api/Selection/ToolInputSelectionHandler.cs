using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Selection;

public static class ToolInputSelectionHandler
{
    public static SelectionResult HandleInput(
        Model model,
        string? cachedSelectionId,
        string? useCurrentSelectionString,
        string? elementIds,
        string? cursor,
        int pageSize,
        int offset,
        ISelectionCacheManager selectionCacheManager)
    {
        ParsePaginationParameters(cursor, ref offset, ref pageSize, out var cursorSelectionId);
        var useCurrentSelection = ParseBoolean(useCurrentSelectionString);

        List<int> idsList;
        if (!string.IsNullOrWhiteSpace(cursorSelectionId))
        {
            if (!selectionCacheManager.TryGetIdsBySelectionId(cursorSelectionId, out idsList) || idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No IDs found for the provided cursor selectionId.",
                    Data = cursorSelectionId
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(cachedSelectionId))
        {
            if (!selectionCacheManager.TryGetIdsBySelectionId(cachedSelectionId, out idsList) || idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No IDs found for the provided cached selectionId.",
                    Data = cachedSelectionId
                };
            }
        }
        else if (useCurrentSelection)
        {
            idsList = new List<int>();
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();
            while (selector.MoveNext())
            {
                if (selector.Current is Tekla.Structures.Model.ModelObject selectedObject)
                    idsList.Add(selectedObject.Identifier.ID);
            }

            if (idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No objects are currently selected in the model."
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(elementIds))
        {
            idsList = ParseElementIds(elementIds);
            if (idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No valid element IDs were provided in elementIds parameter.",
                    Data = elementIds
                };
            }
        }
        else
        {
            return new SelectionResult
            {
                Success = false,
                Message = "No source of IDs was provided. Use cachedSelectionId, useCurrentSelection=true, or elementIds."
            };
        }

        return BuildFinalSelectionResult(idsList, cursorSelectionId, cachedSelectionId, elementIds, offset, pageSize, selectionCacheManager);
    }

    public static SelectionResult HandleInput(
        DrawingHandler drawingHandler,
        string? cachedSelectionId,
        string? useCurrentSelectionString,
        string? elementIds,
        string? cursor,
        int pageSize,
        int offset,
        ISelectionCacheManager selectionCacheManager)
    {
        ParsePaginationParameters(cursor, ref offset, ref pageSize, out var cursorSelectionId);
        var useCurrentSelection = ParseBoolean(useCurrentSelectionString);

        List<int> idsList;
        if (!string.IsNullOrWhiteSpace(cursorSelectionId))
        {
            if (!selectionCacheManager.TryGetIdsBySelectionId(cursorSelectionId, out idsList) || idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No IDs found for the provided cursor selectionId.",
                    Data = cursorSelectionId
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(cachedSelectionId))
        {
            if (!selectionCacheManager.TryGetIdsBySelectionId(cachedSelectionId, out idsList) || idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No IDs found for the provided cached selectionId.",
                    Data = cachedSelectionId
                };
            }
        }
        else if (useCurrentSelection)
        {
            idsList = new List<int>();
            var selector = drawingHandler.GetDrawingObjectSelector().GetSelected();
            while (selector.MoveNext())
            {
                if (selector.Current is DrawingObject selectedObject)
                    idsList.Add(selectedObject.GetIdentifier().ID);
            }

            if (idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No objects are currently selected in the drawing."
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(elementIds))
        {
            idsList = ParseElementIds(elementIds);
            if (idsList.Count == 0)
            {
                return new SelectionResult
                {
                    Success = false,
                    Message = "No valid element IDs were provided in elementIds parameter.",
                    Data = elementIds
                };
            }
        }
        else
        {
            return new SelectionResult
            {
                Success = false,
                Message = "No source of IDs was provided. Use cachedSelectionId, useCurrentSelection=true, or elementIds."
            };
        }

        return BuildFinalSelectionResult(idsList, cursorSelectionId, cachedSelectionId, elementIds, offset, pageSize, selectionCacheManager);
    }

    public static Dictionary<string, object> CreatePaginationMetadata(SelectionResult selectionResult, int offset, int pageSize)
    {
        var meta = new Dictionary<string, object>
        {
            ["selectionId"] = selectionResult.EffectiveSelectionId ?? string.Empty,
            ["total"] = selectionResult.Total,
            ["offset"] = offset,
            ["pageSize"] = pageSize,
            ["hasMore"] = selectionResult.HasMore,
            ["nextOffset"] = selectionResult.HasMore ? (offset + selectionResult.Ids.Count) : offset
        };

        var nextCursorObj = new Dictionary<string, object>
        {
            ["selectionId"] = selectionResult.EffectiveSelectionId ?? string.Empty,
            ["offset"] = selectionResult.HasMore ? (offset + selectionResult.Ids.Count) : offset,
            ["pageSize"] = pageSize
        };

        var nextCursorJson = JsonSerializer.Serialize(nextCursorObj);
        var nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(nextCursorJson));
        meta["nextCursor"] = nextCursor;

        return meta;
    }

    private static SelectionResult BuildFinalSelectionResult(
        List<int> idsList,
        string? cursorSelectionId,
        string? cachedSelectionId,
        string? elementIds,
        int offset,
        int pageSize,
        ISelectionCacheManager selectionCacheManager)
    {
        if (idsList.Count == 0)
        {
            return new SelectionResult
            {
                Success = false,
                Message = "No valid element IDs were provided.",
                Data = elementIds
            };
        }

        idsList = idsList.Distinct().OrderBy(id => id).ToList();

        var effectiveSelectionId = cursorSelectionId;
        if (string.IsNullOrWhiteSpace(effectiveSelectionId))
        {
            effectiveSelectionId = !string.IsNullOrWhiteSpace(cachedSelectionId)
                ? cachedSelectionId
                : string.IsNullOrWhiteSpace(elementIds)
                    ? selectionCacheManager.CreateSelection(idsList)
                    : elementIds;
        }

        var total = idsList.Count;
        if (offset > total)
            offset = total;

        var idsToProcess = idsList.Skip(offset).Take(pageSize).ToList();
        var hasMore = offset + idsToProcess.Count < total;

        return new SelectionResult
        {
            Success = true,
            Ids = idsToProcess,
            EffectiveSelectionId = effectiveSelectionId,
            Total = total,
            HasMore = hasMore,
            Message = "IDs retrieved successfully."
        };
    }

    private static void ParsePaginationParameters(string? cursor, ref int offset, ref int pageSize, out string? cursorSelectionId)
    {
        cursorSelectionId = null;

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            try
            {
                var cursorJson = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                using var doc = JsonDocument.Parse(cursorJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("selectionId", out var sid))
                        cursorSelectionId = sid.GetString();
                    if (doc.RootElement.TryGetProperty("offset", out var off) && off.TryGetInt32(out var parsedOffset))
                        offset = parsedOffset;
                    if (doc.RootElement.TryGetProperty("pageSize", out var size) && size.TryGetInt32(out var parsedPageSize))
                        pageSize = parsedPageSize;
                }
            }
            catch
            {
                // ignore invalid cursor format
            }
        }

        if (pageSize <= 0)
            pageSize = 100;
        if (offset < 0)
            offset = 0;
    }

    private static bool ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind == JsonValueKind.True)
                return true;
            if (doc.RootElement.ValueKind == JsonValueKind.False)
                return false;
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static List<int> ParseElementIds(string elementIds)
    {
        return elementIds
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }
}
