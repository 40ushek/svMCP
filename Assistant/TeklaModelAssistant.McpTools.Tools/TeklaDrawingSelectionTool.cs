using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for selecting objects within Tekla drawings.")]
	public class TeklaDrawingSelectionTool
	{
		[Description("Selects objects in the active drawing based on a list of model object IDs.")]
		public static ToolExecutionResult SelectDrawingObjects([Description("A comma-separated list of explicit model object IDs to select in the drawing.")] string elementIds, [Description("A selection identifier from a previous tool call to reference cached model IDs.")] string cachedSelectionId, ISelectionCacheManager selectionCacheManager)
		{
			DrawingHandler drawingHandler = new DrawingHandler();
			Drawing activeDrawing = drawingHandler.GetActiveDrawing();
			if (activeDrawing == null)
			{
				return ToolExecutionResult.CreateErrorResult("No drawing is currently open. This command requires an active drawing.");
			}
			Model model = new Model();
			SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, "false", elementIds, null, int.MaxValue, 0, selectionCacheManager);
			if (!selectionResult.Success || selectionResult.Ids.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult(selectionResult.Message ?? "No object IDs were provided or found in the cache.");
			}
			HashSet<int> targetModelIds = new HashSet<int>(selectionResult.Ids);
			ArrayList drawingObjectsToSelect = new ArrayList();
			DrawingObjectEnumerator allDrawingObjects = activeDrawing.GetSheet().GetAllObjects();
			while (allDrawingObjects.MoveNext())
			{
				if (allDrawingObjects.Current is Tekla.Structures.Drawing.ModelObject drawingModelObject && targetModelIds.Contains(drawingModelObject.ModelIdentifier.ID))
				{
					drawingObjectsToSelect.Add(drawingModelObject);
				}
			}
			if (drawingObjectsToSelect.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult("None of the specified object IDs could be found as objects in the active drawing.");
			}
			DrawingObjectSelector selector = drawingHandler.GetDrawingObjectSelector();
			selector.SelectObjects(drawingObjectsToSelect, false);
			activeDrawing.CommitChanges("(TMA) SelectDrawingObjects");
			return ToolExecutionResult.CreateSuccessResult($"Selected {drawingObjectsToSelect.Count} objects in the drawing.");
		}
	}
}
