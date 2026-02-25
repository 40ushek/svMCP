using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Managers;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class ToolInputSelectionHandler
	{
		public static SelectionResult HandleInput(Model model, string cachedSelectionId, string useCurrentSelectionString, string elementIds, string cursor, int pageSize, int offset, ISelectionCacheManager selectionCacheManager)
		{
			ParsePaginationParameters(cursor, ref offset, ref pageSize, out var cursorSelectionId);
			useCurrentSelectionString.TryConvertFromJson(out var useCurrentSelection);
			List<int> idsList = new List<int>();
			if (!string.IsNullOrWhiteSpace(cursorSelectionId))
			{
				if (!selectionCacheManager.TryGetIdsBySelectionId(cursorSelectionId, out idsList) || idsList == null || idsList.Count == 0)
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
				if (!selectionCacheManager.TryGetIdsBySelectionId(cachedSelectionId, out idsList) || idsList == null || idsList.Count == 0)
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
				Tekla.Structures.Model.UI.ModelObjectSelector modelObjectSelector = new Tekla.Structures.Model.UI.ModelObjectSelector();
				ModelObjectEnumerator enumerator = modelObjectSelector.GetSelectedObjects();
				while (enumerator.MoveNext())
				{
					Tekla.Structures.Model.ModelObject selectedObject = enumerator.Current;
					if (selectedObject != null)
					{
						idsList.Add(selectedObject.Identifier.ID);
					}
				}
				if (idsList.Count == 0)
				{
					return new SelectionResult
					{
						Success = false,
						Message = "No objects are currently selected in the model.",
						Data = null
					};
				}
			}
			else if (!string.IsNullOrWhiteSpace(elementIds))
			{
				string[] parts = elementIds.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				string[] array = parts;
				foreach (string part in array)
				{
					if (int.TryParse(part, out var id))
					{
						idsList.Add(id);
					}
				}
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
				Tekla.Structures.Model.UI.View currentView = ViewHandler.GetActiveView();
				AABB viewBox = currentView.WorkArea;
				Tekla.Structures.Model.ModelObjectSelector selector = model.GetModelObjectSelector();
				ModelObjectEnumerator enumerator2 = selector.GetObjectsByBoundingBox(viewBox.MinPoint, viewBox.MaxPoint);
				while (enumerator2.MoveNext())
				{
					Tekla.Structures.Model.ModelObject obj = enumerator2.Current;
					if (obj != null)
					{
						idsList.Add(obj.Identifier.ID);
					}
				}
			}
			return BuildFinalSelectionResult(idsList, cursorSelectionId, cachedSelectionId, elementIds, offset, pageSize, selectionCacheManager);
		}

		public static SelectionResult HandleInput(DrawingHandler drawingHandler, string cachedSelectionId, string useCurrentSelectionString, string elementIds, string cursor, int pageSize, int offset, ISelectionCacheManager selectionCacheManager)
		{
			ParsePaginationParameters(cursor, ref offset, ref pageSize, out var cursorSelectionId);
			useCurrentSelectionString.TryConvertFromJson(out var useCurrentSelection);
			List<int> idsList = new List<int>();
			if (!string.IsNullOrWhiteSpace(cursorSelectionId))
			{
				if (!selectionCacheManager.TryGetIdsBySelectionId(cursorSelectionId, out idsList) || idsList == null || idsList.Count == 0)
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
				if (!selectionCacheManager.TryGetIdsBySelectionId(cachedSelectionId, out idsList) || idsList == null || idsList.Count == 0)
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
				DrawingObjectSelector drawingsObjectSelector = drawingHandler.GetDrawingObjectSelector();
				DrawingObjectEnumerator enumerator = drawingsObjectSelector.GetSelected();
				while (enumerator.MoveNext())
				{
					DrawingObject selectedObject = enumerator.Current;
					if (selectedObject != null)
					{
						idsList.Add(selectedObject.GetIdentifier().ID);
					}
				}
				if (idsList.Count == 0)
				{
					return new SelectionResult
					{
						Success = false,
						Message = "No objects are currently selected in the model.",
						Data = null
					};
				}
			}
			else if (!string.IsNullOrWhiteSpace(elementIds))
			{
				string[] parts = elementIds.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				string[] array = parts;
				foreach (string part in array)
				{
					if (int.TryParse(part, out var id))
					{
						idsList.Add(id);
					}
				}
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
				Drawing drawing = drawingHandler.GetActiveDrawing();
				if (drawing != null)
				{
					ViewBase currentView = drawing.GetSheet().GetView();
					if (currentView is Tekla.Structures.Drawing.View view)
					{
						AABB viewBox = view.GetAxisAlignedBoundingBox();
						DrawingObjectEnumerator drawingObjects = drawing.GetSheet().GetAllObjects();
						while (drawingObjects.MoveNext())
						{
							DrawingObject drawingObject = drawingObjects.Current;
							if (IsWithinBoundingBox(drawingObject, viewBox))
							{
								idsList.Add(drawingObject.GetIdentifier().ID);
							}
						}
					}
				}
			}
			return BuildFinalSelectionResult(idsList, cursorSelectionId, cachedSelectionId, elementIds, offset, pageSize, selectionCacheManager);
		}

		private static SelectionResult BuildFinalSelectionResult(List<int> idsList, string cursorSelectionId, string cachedSelectionId, string elementIds, int offset, int pageSize, ISelectionCacheManager selectionCacheManager)
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
			idsList = (from id in idsList.Distinct()
				orderby id
				select id).ToList();
			string effectiveSelectionId = cursorSelectionId;
			if (string.IsNullOrWhiteSpace(effectiveSelectionId))
			{
				effectiveSelectionId = ((!string.IsNullOrWhiteSpace(cachedSelectionId)) ? cachedSelectionId : (string.IsNullOrWhiteSpace(elementIds) ? selectionCacheManager.CreateSelection(idsList) : elementIds));
			}
			int total = idsList.Count;
			if (offset > total)
			{
				offset = total;
			}
			List<int> idsToProcess = idsList.Skip(offset).Take(pageSize).ToList();
			bool hasMore = offset + idsToProcess.Count < total;
			return new SelectionResult
			{
				Success = true,
				Ids = idsToProcess,
				EffectiveSelectionId = effectiveSelectionId,
				Total = total,
				HasMore = hasMore,
				Message = "IDs retrieved successfully.",
				Data = null
			};
		}

		private static void ParsePaginationParameters(string cursor, ref int offset, ref int pageSize, out string cursorSelectionId)
		{
			cursorSelectionId = null;
			if (!string.IsNullOrEmpty(cursor))
			{
				try
				{
					string cursorJson = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
					Dictionary<string, object> cursorObj = JsonSerializer.Deserialize<Dictionary<string, object>>(cursorJson);
					if (cursorObj != null)
					{
						if (cursorObj.ContainsKey("selectionId"))
						{
							cursorSelectionId = cursorObj["selectionId"]?.ToString();
						}
						if (cursorObj.ContainsKey("offset"))
						{
							int.TryParse(cursorObj["offset"]?.ToString(), out offset);
						}
						if (cursorObj.ContainsKey("pageSize"))
						{
							int.TryParse(cursorObj["pageSize"]?.ToString(), out pageSize);
						}
					}
				}
				catch
				{
				}
			}
			if (pageSize <= 0)
			{
				pageSize = 100;
			}
			if (offset < 0)
			{
				offset = 0;
			}
		}

		private static bool IsWithinBoundingBox(DrawingObject obj, AABB viewBox)
		{
			AABB objectBox = null;
			if (obj is Text text)
			{
				objectBox = text.GetAxisAlignedBoundingBox();
			}
			else if (obj is Symbol symbol)
			{
				objectBox = symbol.GetAxisAlignedBoundingBox();
			}
			else if (obj is WeldMark weldMark)
			{
				objectBox = weldMark.GetAxisAlignedBoundingBox();
			}
			if (objectBox == null)
			{
				return false;
			}
			return objectBox.MinPoint.X >= viewBox.MinPoint.X && objectBox.MaxPoint.X <= viewBox.MaxPoint.X && objectBox.MinPoint.Y >= viewBox.MinPoint.Y && objectBox.MaxPoint.Y <= viewBox.MaxPoint.Y;
		}

		public static Dictionary<string, object> CreatePaginationMetadata(SelectionResult selectionResult, int offset, int pageSize)
		{
			Dictionary<string, object> meta = new Dictionary<string, object>
			{
				{ "selectionId", selectionResult.EffectiveSelectionId },
				{ "total", selectionResult.Total },
				{ "offset", offset },
				{ "pageSize", pageSize },
				{ "hasMore", selectionResult.HasMore },
				{
					"nextOffset",
					selectionResult.HasMore ? (offset + selectionResult.Ids.Count) : offset
				}
			};
			Dictionary<string, object> nextCursorObj = new Dictionary<string, object>
			{
				{ "selectionId", selectionResult.EffectiveSelectionId },
				{
					"offset",
					selectionResult.HasMore ? (offset + selectionResult.Ids.Count) : offset
				},
				{ "pageSize", pageSize }
			};
			string nextCursorJson = JsonSerializer.Serialize(nextCursorObj);
			string nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(nextCursorJson));
			meta["nextCursor"] = nextCursor;
			return meta;
		}
	}
}
