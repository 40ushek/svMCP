using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using Tekla.Structures.ModelInternal;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for zooming to objects in Tekla Structures views")]
	public class TeklaZoomTools
	{
		[Description("Zoom to currently selected objects in the view")]
		public static ToolExecutionResult ZoomSelected()
		{
			try
			{
				Operation.dotStartAction("ZoomToSelected", "");
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("Zoom failed.", ex.Message);
			}
			return ToolExecutionResult.CreateSuccessResult("Zoom to selected objects executed successfully.");
		}

		[Description("Select objects by IDs and zoom to them")]
		public static ToolExecutionResult SelectAndZoom([Description("Selection identifier from previous tool")] string cachedSelectionId, [Description("Use current selection (true/false)")] string useCurrentSelectionString, [Description("Comma-separated element IDs")] string elementIds, ISelectionCacheManager selectionCacheManager)
		{
			Model model = new Model();
			if (!model.GetConnectionStatus())
			{
				return ToolExecutionResult.CreateErrorResult("No model is open.");
			}
			SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, null, int.MaxValue, 0, selectionCacheManager);
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
						message += $"Object with ID {id} not found in model or is not an ModelObject.{Environment.NewLine}";
					}
				}
				Tekla.Structures.Model.UI.ModelObjectSelector modelObjectSelector = new Tekla.Structures.Model.UI.ModelObjectSelector();
				bool bSuccess = modelObjectSelector.Select(objectsToSelect);
				Operation.dotStartAction("ZoomToSelected", "");
				return new ToolExecutionResult
				{
					Success = bSuccess,
					Message = "ZoomToSelected executed with " + (bSuccess ? "success" : "failure") + ". Check data property for warnings.",
					Data = message
				};
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while trying to zoom to selected objects.", ex.Message);
			}
		}
	}
}
