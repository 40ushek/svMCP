using System;
using System.ComponentModel;
using System.IO;
using Tekla.Structures.Filtering;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for filtering and selecting objects in a Tekla Structures model. IMPORTANT: Do not use it in drawing mode!")]
	public class TeklaSetViewFilterTool
	{
		[Description("Create and save a PERSISTENT view filter that can be reused across sessions. ONLY use this tool when the user EXPLICITLY asks to 'create a filter', 'make a filter', 'save a filter', or mentions filter by name. DO NOT use for simple visibility requests like 'hide X' or 'show Y' - those should use temporary visibility tools. Examples when to use: 'Create a filter for columns', 'Make a view filter named MyFilter'. Examples when NOT to use: 'Hide beams', 'Show only plates', 'Hide elements on level 0' - use temporary visibility instead.")]
		public static ToolExecutionResult SetViewFilter([Description("A single string containing all filter expressions, separated by semicolons ';'. Within each expression, the parts (Category, Property, Operator, Value, LogicalOperator) are separated by pipes '|'. The LogicalOperator is optional for the last expression. Example: 'Part|PROFILE|IS_EQUAL|HEA300|AND;Part|CLASS|IS_EQUAL|3'")] string filterCriteria, [Description("The view filter name. If not provided this is a default value: DefaultAIViewFilter")] string filterName = "DefaultAIViewFilter")
		{
			Model model = new Model();
			if (string.IsNullOrWhiteSpace(filterCriteria))
			{
				return ToolExecutionResult.CreateErrorResult("The 'filterCriteria' argument is required and cannot be empty.");
			}
			try
			{
				BinaryFilterExpressionCollection filterCollection = FilterHelper.BuildFilterExpressionsWithParentheses(filterCriteria);
				if (filterCollection != null && filterCollection.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("Could not create any valid filter expressions from the provided input.");
				}
				Filter filter = new Filter(filterCollection);
				string modelPath = model.GetInfo().ModelPath;
				string attributesPath = Path.Combine(modelPath, "attributes");
				string filterFilePath = Path.Combine(attributesPath, filterName);
				filter.CreateFile(FilterExpressionFileType.OBJECT_GROUP_VIEW, filterFilePath);
				View view = ViewHandler.GetActiveView();
				view.ViewFilter = filterName;
				view.Modify();
				return ToolExecutionResult.CreateSuccessResult("View filter '" + filterName + "' has been created and applied to the current view. Objects matching the filter criteria will be shown in the view.");
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while creating or applying the view filter.", ex.Message);
			}
		}
	}
}
