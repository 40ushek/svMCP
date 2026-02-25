using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool for temporarily controlling visibility of specific objects that have already been selected or filtered.")]
	public class TeklaSetTemporaryVisibilityTool
	{
		[Description("TEMPORARILY control visibility of SPECIFIC objects using their IDs from previous filter or selection operations. Use this when you need to temporarily control visibility of objects you've already identified with FilterObjects tool. For controlling visibility of all objects matching criteria → First use FilterObjects, then use this tool with the returned cachedSelectionId or elementIds.")]
		public static ToolExecutionResult SetTemporaryVisibility([Description("Selection identifier referencing previously stored IDs from filter operations.")] string cachedSelectionId, [Description("Comma-separated list of explicit element IDs.")] string elementIds, [Description("Operation to perform: 'hide', 'show', or 'showonly'")] string operation, ISelectionCacheManager selectionCacheManager)
		{
			Model model = new Model();
			string[] validOperations = new string[3] { "hide", "show", "showonly" };
			if (string.IsNullOrWhiteSpace(operation) || !validOperations.Contains(operation.ToLower()))
			{
				return ToolExecutionResult.CreateErrorResult("Invalid operation '" + operation + "'. Valid operations are: " + string.Join(", ", validOperations));
			}
			SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, "false", elementIds, null, int.MaxValue, 0, selectionCacheManager);
			if (!selectionResult.Success)
			{
				return ToolExecutionResult.CreateErrorResult(selectionResult.Message);
			}
			try
			{
				List<ModelObject> modelObjects = new List<ModelObject>();
				foreach (int id in selectionResult.Ids)
				{
					ModelObject modelObject = model.SelectModelObject(new Identifier(id));
					if (modelObject != null)
					{
						modelObjects.Add(modelObject);
					}
				}
				if (modelObjects.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No valid objects found.");
				}
				int objectCount = modelObjects.Count;
				bool success;
				string resultMessage;
				switch (operation.ToLower())
				{
				case "hide":
					success = ModelObjectVisualization.SetVisibility(modelObjects, false);
					resultMessage = $"Hidden {objectCount} object(s).";
					break;
				case "show":
					success = ModelObjectVisualization.SetVisibility(modelObjects, true);
					resultMessage = $"Made {objectCount} object(s) visible.";
					break;
				case "showonly":
					ModelObjectVisualization.SetTransparencyForAll(TemporaryTransparency.HIDDEN);
					success = ModelObjectVisualization.SetVisibility(modelObjects, true);
					resultMessage = $"Showing only {objectCount} object(s). All other objects are hidden.";
					break;
				default:
					return ToolExecutionResult.CreateErrorResult("Unknown operation: " + operation);
				}
				return new ToolExecutionResult
				{
					Success = success,
					Message = resultMessage
				};
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while changing object visibility: " + ex.Message);
			}
		}
	}
}
