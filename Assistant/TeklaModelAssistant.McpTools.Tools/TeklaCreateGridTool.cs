using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to create grid.")]
	public class TeklaCreateGridTool
	{
		[Description("Creates grid based on the provided parameters.")]
		public static ToolExecutionResult CreateGrid([Description("Property set of the grid to be created. It is a dictionary in Json format.Key is the property name, value is its value.CRITICAL: Do NOT include optional properties (Name, Profile, Material, Class, etc.) in the JSON unless the user explicitly requests them as overrides.")] string propertySetString)
		{
			Model model = new Model();
			if (!propertySetString.TryConvertFromJson<Dictionary<string, string>>(out var propertySet))
			{
				return ToolExecutionResult.CreateErrorResult("Failed to parse 'propertySetString' argument. Ensure it is a valid JSON dictionary string.");
			}
			Grid grid = new Grid();
			StringBuilder messageBuilder = new StringBuilder();
			foreach (KeyValuePair<string, string> kvp in propertySet)
			{
				try
				{
					if (!PropertyAccessHelper.TrySetPropertyValue(grid, kvp.Key, kvp.Value))
					{
						messageBuilder.AppendLine("Warning: '" + kvp.Key + "' could not be set");
					}
				}
				catch (Exception ex)
				{
					messageBuilder.AppendLine("Error: '" + kvp.Key + "' could not be set. " + ex.Message);
				}
			}
			if (!grid.Insert())
			{
				return ToolExecutionResult.CreateErrorResult("Failed to create the grid in the model.");
			}
			model.CommitChanges("TMA: CreateGrid");
			return ToolExecutionResult.CreateSuccessResult($"Grid with id {grid.Identifier} created successfully.", new
			{
				AdditionalInfo = messageBuilder.ToString()
			});
		}
	}
}
