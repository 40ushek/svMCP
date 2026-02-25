using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("A tool to modify properties of Tekla model objects or drawing objects")]
	public class SetElementPropertiesTool
	{
		[Description("Sets one or more properties for multiple elements in the model.")]
		public static ToolExecutionResult SetElementProperties([Description("A JSON string defining the properties and new values to set. Example: '{ \"Class\": \"3\", \"Name\": \"BEAM\" }'")] string propertiesToSetJson, [Description("Selection identifier referencing previously stored IDs")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query")] string elementIds, [Description("Opaque base64-encoded paging token (overrides offset/pageSize)")] string cursor, [Description("The number of ids to process in one run (default 100)")] int pageSize, [Description("The offset to start retrieving items from (default 0)")] int offset, ISelectionCacheManager selectionCacheManager)
		{
			if (string.IsNullOrWhiteSpace(propertiesToSetJson))
			{
				return ToolExecutionResult.CreateErrorResult("The 'propertiesToSetJson' argument is required and cannot be empty.");
			}
			Dictionary<string, string> propertiesToSet;
			try
			{
				propertiesToSet = JsonDocument.Parse(propertiesToSetJson).RootElement.Deserialize<Dictionary<string, string>>();
				if (propertiesToSet == null || propertiesToSet.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("Parsed 'propertiesToSet' is null or empty. Ensure the JSON string is a valid, non-empty object.");
				}
			}
			catch (JsonException ex)
			{
				return ToolExecutionResult.CreateErrorResult("Failed to parse 'propertiesToSetJson' as a valid JSON object.", ex.Message);
			}
			try
			{
				bool isDrawingMode = false;
				DrawingHandler drawingHandler = new DrawingHandler();
				Model model = new Model();
				if (drawingHandler.GetConnectionStatus())
				{
					Drawing activeDrawing = drawingHandler.GetActiveDrawing();
					if (activeDrawing != null)
					{
						isDrawingMode = true;
					}
					else if (!model.GetConnectionStatus())
					{
						return ToolExecutionResult.CreateErrorResult("No active drawing found and unable to connect to the Tekla model.");
					}
				}
				SelectionResult selectionResult = (isDrawingMode ? ToolInputSelectionHandler.HandleInput(drawingHandler, cachedSelectionId, useCurrentSelectionString, elementIds, cursor, pageSize, offset, selectionCacheManager) : ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, cursor, pageSize, offset, selectionCacheManager));
				if (!selectionResult.Success)
				{
					return new ToolExecutionResult
					{
						Success = selectionResult.Success,
						Message = selectionResult.Message,
						Data = selectionResult.Data,
						Error = selectionResult.Message
					};
				}
				List<int> updatedObjectIds;
				Dictionary<int, List<string>> errors;
				if (isDrawingMode)
				{
					(updatedObjectIds, errors) = SetDrawingObjectProperties(drawingHandler, selectionResult, propertiesToSet);
				}
				else
				{
					(updatedObjectIds, errors) = SetModelObjectProperties(model, selectionResult, propertiesToSet);
				}
				return BuildOptimizedResponse(updatedObjectIds, errors, selectionResult, offset, pageSize, selectionCacheManager);
			}
			catch (Exception ex2)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while setting element properties.", ex2.Message);
			}
		}

		private static ToolExecutionResult BuildOptimizedResponse(List<int> updatedObjectIds, Dictionary<int, List<string>> errors, SelectionResult selectionResult, int offset, int pageSize, ISelectionCacheManager cacheManager)
		{
			int successCount = updatedObjectIds.Count;
			int errorCount = errors.Count;
			bool isTotalFailure = successCount == 0 && errorCount > 0;
			string failedSelectionId = null;
			List<string> commonErrorMessages = new List<string>();
			if (errorCount > 20)
			{
				commonErrorMessages = errors.Values.SelectMany((List<string> e) => e).Distinct().Take(5)
					.ToList();
				if (!isTotalFailure)
				{
					List<int> failedIds = errors.Keys.ToList();
					failedSelectionId = cacheManager.CreateSelection(failedIds);
				}
				errors.Clear();
			}
			Dictionary<string, object> meta = ToolInputSelectionHandler.CreatePaginationMetadata(selectionResult, offset, pageSize);
			Dictionary<string, object> resultData = new Dictionary<string, object>
			{
				{ "successCount", successCount },
				{ "errorCount", errorCount },
				{ "meta", meta }
			};
			if (errorCount > 0)
			{
				if (failedSelectionId != null)
				{
					resultData.Add("failedSelectionId", failedSelectionId);
					resultData.Add("commonErrorCauses", commonErrorMessages);
					resultData.Add("note", "Partial failure. Failed items saved to failedSelectionId.");
				}
				else if (isTotalFailure && errorCount > 20)
				{
					resultData.Add("commonErrorCauses", commonErrorMessages);
					resultData.Add("note", "Total failure. All items failed. See commonErrorCauses.");
				}
				else
				{
					var groupedErrors = (from x in errors.SelectMany((KeyValuePair<int, List<string>> kvp) => kvp.Value.Select((string msg) => new
						{
							Id = kvp.Key,
							Message = msg
						}))
						group x by x.Message into g
						select new
						{
							Error = g.Key,
							Count = g.Count(),
							AffectedIds = g.Select(x => x.Id).ToList()
						}).ToList();
					resultData.Add("errors", groupedErrors);
				}
			}
			StringBuilder summary = new StringBuilder();
			int distinctObjectsUpdated = updatedObjectIds.Distinct().Count();
			if (isTotalFailure)
			{
				summary.Append($"Operation FAILED. 0 items updated. {errorCount} items failed. ");
			}
			else
			{
				summary.Append($"Operation finished. Updated {distinctObjectsUpdated} items. ");
				if (errorCount > 0)
				{
					summary.Append($"Failed to update {errorCount} items. ");
				}
				if (failedSelectionId != null)
				{
					summary.Append("Failures saved to cache ID: '" + failedSelectionId + "'. ");
				}
				if (selectionResult.HasMore)
				{
					summary.Append("(More items available).");
				}
			}
			return new ToolExecutionResult
			{
				Success = !isTotalFailure,
				Data = resultData,
				Message = summary.ToString()
			};
		}

		private static void AddError(Dictionary<int, List<string>> errorLog, int id, string message)
		{
			if (!errorLog.ContainsKey(id))
			{
				errorLog[id] = new List<string>();
			}
			errorLog[id].Add(message);
		}

		private static (List<int> updatedObjectIds, Dictionary<int, List<string>> errors) SetDrawingObjectProperties(DrawingHandler drawingHandler, SelectionResult selectionResult, Dictionary<string, string> propertiesToSet)
		{
			bool changesMade = false;
			List<int> updatedObjectIds = new List<int>();
			Dictionary<int, List<string>> errors = new Dictionary<int, List<string>>();
			DrawingObjectEnumerator drawingObjects = drawingHandler.GetActiveDrawing().GetSheet().GetAllObjects();
			while (drawingObjects.MoveNext())
			{
				int id = drawingObjects.Current.GetIdentifier().ID;
				if (!selectionResult.Ids.Contains(id) && (!(drawingObjects.Current is Tekla.Structures.Drawing.ModelObject drawingModelObject) || !selectionResult.Ids.Contains(drawingModelObject.ModelIdentifier.ID)))
				{
					continue;
				}
				foreach (KeyValuePair<string, string> propEntry in propertiesToSet)
				{
					try
					{
						if (!PropertyAccessHelper.TrySetPropertyValue(drawingObjects.Current, propEntry.Key, propEntry.Value))
						{
							AddError(errors, id, "Property '" + propEntry.Key + "' not found or couldn't be set.");
							continue;
						}
						updatedObjectIds.Add(id);
						drawingObjects.Current.Modify();
						changesMade = true;
					}
					catch (Exception ex)
					{
						AddError(errors, id, "Property '" + propEntry.Key + "' was not set." + Environment.NewLine + ex.Message + Environment.NewLine);
					}
				}
			}
			if (changesMade)
			{
				drawingHandler.GetActiveDrawing().CommitChanges("(TMA) SetElementProperties");
			}
			return (updatedObjectIds: updatedObjectIds, errors: errors);
		}

		private static (List<int> updatedObjectIds, Dictionary<int, List<string>> errors) SetModelObjectProperties(Model model, SelectionResult selectionResult, Dictionary<string, string> propertiesToSet)
		{
			IList<int> idsToProcess = selectionResult.Ids;
			bool changesMade = false;
			List<int> updatedObjectIds = new List<int>();
			Dictionary<int, List<string>> errors = new Dictionary<int, List<string>>();
			foreach (int id in idsToProcess)
			{
				Tekla.Structures.Model.ModelObject modelObj = model.SelectModelObject(new Identifier(id));
				if (modelObj == null)
				{
					AddError(errors, id, "Element not found in the model.");
					continue;
				}
				bool objectModifiedInLoop = false;
				foreach (KeyValuePair<string, string> propEntry in propertiesToSet)
				{
					try
					{
						if (!PropertyAccessHelper.TrySetPropertyValue(modelObj, propEntry.Key, propEntry.Value))
						{
							AddError(errors, id, "Property '" + propEntry.Key + "' not found or couldn't be set.");
						}
						else
						{
							objectModifiedInLoop = true;
						}
					}
					catch (Exception ex)
					{
						AddError(errors, id, "Property '" + propEntry.Key + "' was not set." + Environment.NewLine + ex.Message + Environment.NewLine);
					}
				}
				if (objectModifiedInLoop)
				{
					if (modelObj.Modify())
					{
						updatedObjectIds.Add(id);
						changesMade = true;
					}
					else
					{
						AddError(errors, id, "Could not modify the object in the model. This may be due to an invalid property value, such as a non-existent material.");
					}
				}
			}
			if (changesMade)
			{
				model.CommitChanges("(TMA) SetElementProperties");
			}
			return (updatedObjectIds: updatedObjectIds, errors: errors);
		}
	}
}
