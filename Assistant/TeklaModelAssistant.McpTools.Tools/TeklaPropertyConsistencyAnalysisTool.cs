using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for analyzing property value consistency and finding inconsistent or minority values.")]
	public class TeklaPropertyConsistencyAnalysisTool
	{
		[Description("Checks property value consistency across selected elements. Returns a simple report showing each property value, its count, and whether it's the norm or an outlier.")]
		public static ToolExecutionResult CheckPropertyConsistency([Description("Selection identifier referencing previously stored IDs")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query")] string elementIds, [Description("The property name to check (e.g., 'Profile.ProfileString', 'Class', 'Name')")] string propertyName, ISelectionCacheManager selectionCacheManager)
		{
			if (string.IsNullOrWhiteSpace(propertyName))
			{
				return ToolExecutionResult.CreateErrorResult("Property name cannot be empty.");
			}
			try
			{
				Model model = new Model();
				SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, null, int.MaxValue, 0, selectionCacheManager);
				if (!selectionResult.Success)
				{
					return ToolExecutionResult.CreateErrorResult(selectionResult.Message, null, selectionResult.Data);
				}
				if (selectionResult.Ids == null || selectionResult.Ids.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No elements found to analyze.");
				}
				Dictionary<int, string> propertyValues = new Dictionary<int, string>();
				List<int> notFoundIds = new List<int>();
				Dictionary<string, int> valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				PropertyAccessHelper.CollectPropertyValues(model, selectionResult.Ids, propertyName, propertyValues, notFoundIds, valueCounts);
				if (valueCounts.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No valid property values found for '" + propertyName + "'.");
				}
				int totalElements = selectionResult.Ids.Count;
				int maxCount = valueCounts.Values.Max();
				int outlierThreshold;
				if (totalElements < 100)
				{
					outlierThreshold = Math.Max(1, maxCount / 10);
				}
				else
				{
					outlierThreshold = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(totalElements)));
				}
				List<Dictionary<string, object>> consistencyReport = valueCounts.OrderByDescending((KeyValuePair<string, int> kvp) => kvp.Value).Select(delegate(KeyValuePair<string, int> kvp)
				{
					double value = (double)kvp.Value / (double)totalElements * 100.0;
					bool flag = valueCounts.Count > 1 && kvp.Value < outlierThreshold;
					return new Dictionary<string, object>
					{
						{ "property", propertyName },
						{ "value", kvp.Key },
						{ "count", kvp.Value },
						{
							"percentage",
							Math.Round(value, 2)
						},
						{
							"status",
							flag ? "outlier" : "norm"
						}
					};
				}).ToList();
				return new ToolExecutionResult
				{
					Success = true,
					Message = "Property consistency analysis completed for '" + propertyName + "'.",
					Data = new Dictionary<string, object>
					{
						{ "consistencyReport", consistencyReport },
						{ "totalElements", totalElements },
						{ "uniqueValues", valueCounts.Count }
					}
				};
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred during property consistency analysis.", ex.Message);
			}
		}
	}
}
