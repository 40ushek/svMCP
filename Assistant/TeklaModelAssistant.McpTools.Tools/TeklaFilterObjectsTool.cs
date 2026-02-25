using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures.Filtering;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for filtering and selecting objects in a Tekla Structures model.")]
	public class TeklaFilterObjectsTool
	{
		[Description("Filter Tekla Structures objects based on properties and values. Returns object IDs that match the filter criteria.IMPORTANT: For complex queries like 'concrete objects', you MUST use the full, multi-part structure from the examples (e.g., 'Template|MATERIAL_TYPE|CONTAINS|CONCRETE'). Do not simplify the filter structure, as this will fail.")]
		public static ToolExecutionResult FilterObjects([Description("A single string containing all filter expressions, separated by semicolons ';'. Within each expression, the parts (Category, Property, Operator, Value, LogicalOperator) are separated by pipes '|'. The LogicalOperator is optional for the last expression. Example: 'Part|PROFILE|IS_EQUAL|HEA300|AND;Part|CLASS|IS_EQUAL|3'")] string filterCriteria, ISelectionCacheManager cacheManager)
		{
			Model model = new Model();
			if (string.IsNullOrWhiteSpace(filterCriteria))
			{
				return ToolExecutionResult.CreateErrorResult("The 'filterCriteria' string cannot be empty.");
			}
			try
			{
				BinaryFilterExpressionCollection filterCollection = FilterHelper.BuildFilterExpressionsWithParentheses(filterCriteria);
				if (filterCollection != null && filterCollection.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("Could not create any valid filter expressions from the provided input.");
				}
				ModelObjectSelector selector = model.GetModelObjectSelector();
				ModelObjectEnumerator filteredObjects = selector.GetObjectsByFilter(filterCollection);
				List<int> objectIds = new List<int>();
				foreach (ModelObject modelObject in filteredObjects)
				{
					if (modelObject != null)
					{
						objectIds.Add(modelObject.Identifier.ID);
					}
				}
				if (objectIds.Count > 20)
				{
					string selectionId = cacheManager.CreateSelection(objectIds);
					string preview = string.Join(", ", objectIds.Take(10));
					string summary = string.Format("Found {0} objects. selectionId: {1}. Preview: [{2}{3}]", objectIds.Count, selectionId, preview, (objectIds.Count > 10) ? ", ..." : "");
					return ToolExecutionResult.CreateSuccessResult(summary);
				}
				string resultText = string.Format("Found {0} objects matching filter criteria: [{1}]", objectIds.Count, string.Join(", ", objectIds));
				return ToolExecutionResult.CreateSuccessResult(resultText, objectIds);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while filtering objects.", ex.Message);
			}
		}
	}
}
