using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for filtering drawing objects.")]
	public class TeklaDrawingObjectFilterTool
	{
		[Description("This tool filters drawing objects (e.g., 'Mark', 'Part', 'DimensionBase'). It does NOT filter model objects like beams or columns.")]
		public static ToolExecutionResult FilterDrawingObjects([Description("The general type of drawing object to filter (e.g., 'Mark', 'Part', 'DimensionBase').")] string objectType, [Description("Optional: A specific subtype to filter by. For Marks: 'Part Mark', 'Bolt Mark'.")] string specificType, ISelectionCacheManager selectionCacheManager)
		{
			string[] modelObjectKeywords = new string[5] { "BEAM", "COLUMN", "SLAB", "FOOTING", "CONTOURPLATE" };
			if (!string.IsNullOrWhiteSpace(specificType) && modelObjectKeywords.Contains(specificType.ToUpperInvariant()))
			{
				return ToolExecutionResult.CreateErrorResult("Invalid 'specificType': '" + specificType + "'. This tool only filters drawing objects. To find model objects like beams or columns, you MUST use the 'FilterModelObjects' tool instead. Do not look for excuses to not use it, just do it.");
			}
			DrawingHandler drawingHandler = new DrawingHandler();
			Drawing activeDrawing = drawingHandler.GetActiveDrawing();
			if (activeDrawing == null)
			{
				return ToolExecutionResult.CreateErrorResult("No drawing is currently open.");
			}
			Model model = new Model();
			if (string.IsNullOrWhiteSpace(objectType))
			{
				return ToolExecutionResult.CreateErrorResult("The 'objectType' parameter cannot be empty.");
			}
			List<int> foundObjectIds = DrawingObjectsFilterHelper.FindDrawingObjectsIds(activeDrawing, model, objectType, specificType);
			if (foundObjectIds == null || !foundObjectIds.Any())
			{
				return ToolExecutionResult.CreateErrorResult("No drawing objects of type '" + objectType + "' (subtype: '" + (specificType ?? "any") + "') were found.");
			}
			if (foundObjectIds.Count > 20)
			{
				string selectionId = selectionCacheManager.CreateSelection(foundObjectIds);
				string preview = string.Join(", ", foundObjectIds.Take(10));
				string summary = string.Format("Found {0} objects. selectionId: {1}. Preview: [{2}{3}]", foundObjectIds.Count, selectionId, preview, (foundObjectIds.Count > 10) ? ", ..." : "");
				return ToolExecutionResult.CreateSuccessResult(summary);
			}
			string resultText = string.Format("Found {0} objects matching filter criteria: [{1}]", foundObjectIds.Count, string.Join(", ", foundObjectIds));
			return ToolExecutionResult.CreateSuccessResult(resultText, foundObjectIds);
		}
	}
}
