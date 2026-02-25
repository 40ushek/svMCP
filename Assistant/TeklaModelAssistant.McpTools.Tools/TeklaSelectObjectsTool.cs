using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool for selecting objects in the Tekla Structures model UI.")]
	public class TeklaSelectObjectsTool
	{
		[Description("Physically selects/highlights objects in the Tekla Structures UI by their IDs or cached selection ID. This changes the actual UI selection. DO NOT use this tool if the user only wants to filter/find objects based on criteria - use filter tool for that purpose. Only use this when explicitly asked to 'select' or 'highlight' objects in the UI.")]
		public static ToolExecutionResult SelectObjectsByIds([Description("Selection identifier referencing previously stored IDs.")] string cachedSelectionId, [Description("Comma-separated list of explicit element IDs to select.")] string elementIds, ISelectionCacheManager selectionCacheManager)
		{
			Model model = new Model();
			if (!model.GetConnectionStatus())
			{
				return ToolExecutionResult.CreateErrorResult("No model is open.");
			}
			SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, "false", elementIds, null, int.MaxValue, 0, selectionCacheManager);
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
			try
			{
				IList<int> idsToProcess = selectionResult.Ids;
				ArrayList objectsToSelect = new ArrayList();
				string message = "";
				foreach (int id in idsToProcess)
				{
					ModelObject modelObject = model.SelectModelObject(new Identifier(id));
					if (modelObject != null)
					{
						objectsToSelect.Add(modelObject);
					}
					else
					{
						message += $"Object with ID {id} not found in model or is not a ModelObject.{Environment.NewLine}";
					}
				}
				Tekla.Structures.Model.UI.ModelObjectSelector modelObjectSelector = new Tekla.Structures.Model.UI.ModelObjectSelector();
				bool bSuccess = modelObjectSelector.Select(objectsToSelect);
				string resultMessage = $"Selected {objectsToSelect.Count} object(s).";
				if (!string.IsNullOrEmpty(message))
				{
					resultMessage += " Check data property for warnings.";
				}
				return new ToolExecutionResult
				{
					Success = bSuccess,
					Message = resultMessage,
					Data = (string.IsNullOrEmpty(message) ? null : message)
				};
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while selecting objects.", ex.Message);
			}
		}
	}
}
