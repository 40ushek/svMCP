using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tools for getting element property values")]
	public class TeklaGetElementPropertyValuesTool
	{
		[Description("Retrieve a specific property value for multiple elements. Sources: saved selectionId, current selection(useCurrentSelection = true), explicit ids, or active view. Supports pagination via offset/pageSize or cursor.")]
		public static ToolExecutionResult GetElementPropertyValues([Description("Selection identifier referencing previously stored IDs")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query")] string elementIds, [Description("The property name to retrieve for each element")] string propertyName, [Description("Opaque base64-encoded paging token (overrides offset/pageSize). Null by default.")] string cursor, [Description("The number of ids to process in one run (default 100)")] int pageSize, [Description("The offset to start retrieving items from (default 0)")] int offset, ISelectionCacheManager selectionCacheManager)
		{
			try
			{
				Model model = new Model();
				ToolExecutionResult validationResult = ValidateModelAndPropertyName(model, propertyName);
				if (validationResult != null)
				{
					return validationResult;
				}
				SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, cursor, pageSize, offset, selectionCacheManager);
				if (!selectionResult.Success)
				{
					return ToolExecutionResult.CreateErrorResult(selectionResult.Message, null, selectionResult.Data);
				}
				Dictionary<string, List<int>> groupedValues = new Dictionary<string, List<int>>();
				List<int> notFoundIds = new List<int>();
				ProcessModelObjects(model, selectionResult.Ids, propertyName, groupedValues, notFoundIds);
				ToolExecutionResult response = BuildBaseResponse(groupedValues, notFoundIds, propertyName, selectionCacheManager);
				if (!response.Success)
				{
					return response;
				}
				if (response.Data is Dictionary<string, object> dataDict)
				{
					Dictionary<string, object> meta = ToolInputSelectionHandler.CreatePaginationMetadata(selectionResult, offset, pageSize);
					dataDict["meta"] = meta;
				}
				if (selectionResult.HasMore)
				{
					response.Message += "(More items available).";
				}
				return response;
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while retrieving element property values", ex.Message);
			}
		}

		[Description("Gets the value counts for a given string property. Sources: saved selectionId, current selection (useCurrentSelectionString=true), explicit ids, or entire model.")]
		public static ToolExecutionResult GetElementPropertyValueCount([Description("Selection identifier referencing previously stored IDs.")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures.")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query.")] string elementIds, [Description("The property name to retrieve for each element.")] string propertyName, [Description("Sort by 'asc' (least frequent first) or 'desc' (most frequent first). Default 'desc'.")] string sortOrder, ISelectionCacheManager selectionCacheManager)
		{
			try
			{
				Model model = new Model();
				ToolExecutionResult validationResult = ValidateModelAndPropertyName(model, propertyName);
				if (validationResult != null)
				{
					return validationResult;
				}
				SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, null, int.MaxValue, 0, selectionCacheManager);
				if (!selectionResult.Success)
				{
					return ToolExecutionResult.CreateErrorResult(selectionResult.Message, null, selectionResult.Data);
				}
				Dictionary<string, List<int>> groupedValues = new Dictionary<string, List<int>>();
				List<int> notFoundIds = new List<int>();
				ProcessModelObjects(model, selectionResult.Ids, propertyName, groupedValues, notFoundIds);
				if (groupedValues.Count == 0 && notFoundIds.Count > 0)
				{
					return ToolExecutionResult.CreateErrorResult($"Failed to retrieve property '{propertyName}' for all {notFoundIds.Count} selected items.");
				}
				Dictionary<string, int> valueCounts = groupedValues.ToDictionary((KeyValuePair<string, List<int>> k) => k.Key, (KeyValuePair<string, List<int>> v) => v.Value.Count);
				IEnumerable<KeyValuePair<string, int>> sorted = ((!sortOrder.Equals("asc", StringComparison.InvariantCultureIgnoreCase)) ? (from kv in valueCounts
					orderby kv.Value descending, kv.Key
					select kv) : (from kv in valueCounts
					orderby kv.Value, kv.Key
					select kv));
				var items = (from x in sorted.Take(50)
					select new
					{
						value = x.Key,
						count = x.Value
					}).ToList();
				Dictionary<string, object> jsonPayload = new Dictionary<string, object>
				{
					{ "propertyName", propertyName },
					{ "totalObjectsScanned", selectionResult.Total },
					{ "uniqueValuesFound", valueCounts.Count },
					{ "counts", items }
				};
				if (valueCounts.Count > 50)
				{
					jsonPayload.Add("warning", $"Too many unique values ({valueCounts.Count}). Showing top {50} only.");
				}
				if (notFoundIds.Count > 0)
				{
					jsonPayload.Add("failedCount", notFoundIds.Count);
				}
				return ToolExecutionResult.CreateSuccessResult($"Analyzed {selectionResult.Total} objects. Found {valueCounts.Count} unique values.", jsonPayload);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while counting element property values", ex.Message);
			}
		}

		[Description("Groups objects by a shared property value. Large groups (>10) return a cached selection ID, smaller groups return explicit IDs. Crucially, use this tool for relative modifications where the current value of a property is needed to calculate the new one. Example: 'increase profile by 100' or 'append '_A' to the name'.")]
		public static ToolExecutionResult GroupElementsByProperty([Description("The property name to group elements by (e.g., 'Profile.ProfileString', 'Class').")] string propertyName, [Description("Selection identifier referencing previously stored IDs.")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures.")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query.")] string elementIds, ISelectionCacheManager selectionCacheManager)
		{
			try
			{
				Model model = new Model();
				ToolExecutionResult validationResult = ValidateModelAndPropertyName(model, propertyName);
				if (validationResult != null)
				{
					return validationResult;
				}
				SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, null, int.MaxValue, 0, selectionCacheManager);
				if (!selectionResult.Success)
				{
					return ToolExecutionResult.CreateErrorResult(selectionResult.Message);
				}
				Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>();
				List<int> notFoundIds = new List<int>();
				ProcessModelObjects(model, selectionResult.Ids, propertyName, groups, notFoundIds);
				return BuildBaseResponse(groups, notFoundIds, propertyName, selectionCacheManager);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while grouping elements by property", ex.Message);
			}
		}

		private static ToolExecutionResult ValidateModelAndPropertyName(Model model, string propertyName)
		{
			if (!model.GetConnectionStatus())
			{
				return ToolExecutionResult.CreateErrorResult("Unable to connect to Tekla Structures model.");
			}
			if (string.IsNullOrWhiteSpace(propertyName))
			{
				return ToolExecutionResult.CreateErrorResult("Property name cannot be empty.");
			}
			return null;
		}

		private static void ProcessModelObjects(Model model, IList<int> idsToProcess, string propertyName, Dictionary<string, List<int>> groupedValues, List<int> notFoundIds)
		{
			foreach (int id in idsToProcess)
			{
				ModelObject modelObject = model.SelectModelObject(new Identifier(id));
				object value;
				if (modelObject == null)
				{
					notFoundIds.Add(id);
				}
				else if (PropertyAccessHelper.TryGetPropertyValue(modelObject, propertyName, out value))
				{
					string valKey = value?.ToString()?.Trim() ?? "(null)";
					if (!groupedValues.TryGetValue(valKey, out var idList))
					{
						idList = (groupedValues[valKey] = new List<int>());
					}
					idList.Add(id);
				}
				else
				{
					notFoundIds.Add(id);
				}
			}
		}

		private static ToolExecutionResult BuildBaseResponse(Dictionary<string, List<int>> groupedValues, List<int> notFoundIds, string propertyName, ISelectionCacheManager cacheManager)
		{
			int totalItemsRetrieved = groupedValues.Sum((KeyValuePair<string, List<int>> x) => x.Value.Count);
			int uniqueValueCount = groupedValues.Count;
			if (totalItemsRetrieved == 0 && notFoundIds.Count > 0)
			{
				return ToolExecutionResult.CreateErrorResult($"Failed to retrieve property '{propertyName}' for all {notFoundIds.Count} selected items.");
			}
			bool isTruncated = false;
			Dictionary<string, object> finalValuesData = new Dictionary<string, object>();
			int cachedGroupCount = 0;
			int totalIdsCount = 0;
			IOrderedEnumerable<KeyValuePair<string, List<int>>> sortedGroups = groupedValues.OrderByDescending((KeyValuePair<string, List<int>> g) => g.Value.Count);
			foreach (KeyValuePair<string, List<int>> group in sortedGroups)
			{
				if (totalIdsCount >= 100)
				{
					isTruncated = true;
					break;
				}
				string valueKey = group.Key;
				List<int> ids = group.Value;
				int groupSize = ids.Count;
				bool shouldCache = false;
				if (groupSize > 10 || totalIdsCount + groupSize > 100)
				{
					shouldCache = true;
				}
				if (shouldCache)
				{
					string cacheKey = cacheManager.CreateSelection(ids);
					finalValuesData.Add(valueKey, new Dictionary<string, object>
					{
						{ "count", groupSize },
						{ "selectionId", cacheKey }
					});
					cachedGroupCount++;
					totalIdsCount++;
				}
				else
				{
					finalValuesData.Add(valueKey, ids);
					totalIdsCount += groupSize;
				}
			}
			Dictionary<string, object> resultData = new Dictionary<string, object>
			{
				{ "propertyName", propertyName },
				{ "totalItems", totalItemsRetrieved },
				{ "uniqueValues", uniqueValueCount },
				{ "values", finalValuesData }
			};
			if (notFoundIds.Count > 0)
			{
				if (notFoundIds.Count > 20)
				{
					string failedCacheKey = cacheManager.CreateSelection(notFoundIds);
					resultData.Add("notFoundIds", new Dictionary<string, object>
					{
						{ "count", notFoundIds.Count },
						{ "selectionId", failedCacheKey }
					});
				}
				else
				{
					resultData.Add("notFoundIds", notFoundIds);
				}
			}
			StringBuilder summary = new StringBuilder();
			summary.Append($"Found {uniqueValueCount} distinct values across {totalItemsRetrieved} items. ");
			if (isTruncated)
			{
				summary.Append("Data truncated. Showing top groups. ");
			}
			if (cachedGroupCount > 0)
			{
				summary.Append($"{cachedGroupCount} large groups cached. ");
			}
			if (notFoundIds.Count > 0)
			{
				summary.Append($"{notFoundIds.Count} items could not be retrieved. ");
			}
			return ToolExecutionResult.CreateSuccessResult(summary.ToString(), resultData);
		}
	}
}
