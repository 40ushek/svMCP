using System;
using System.Collections;
using System.ComponentModel;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Providers.ContextProvider;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures context collection tool")]
	public class TeklaAdvancedContextTool
	{
		private class SelectionInfo
		{
			public int TotalCount { get; set; }

			public bool WasLimited { get; set; }

			public ArrayList OriginalSelection { get; set; }
		}

		[Description("Return context based on current object selection: type, modifiable and readonly parameters, position, etc")]
		public static ToolExecutionResult GetCurrentContext()
		{
			try
			{
				bool isDrawingMode = IsInDrawingMode();
				SelectionInfo selectionInfo = (isDrawingMode ? GetSelectedDrawingObjects() : GetSelectedModelObjects());
				string collectedContextJson = (isDrawingMode ? DrawingContextProvider.CollectContext() : ModelContextProvider.CollectContext());
				if (selectionInfo.WasLimited)
				{
					if (isDrawingMode)
					{
						RestoreDrawingSelection(selectionInfo.OriginalSelection);
					}
					else
					{
						RestoreModelSelection(selectionInfo.OriginalSelection);
					}
				}
				string contextMessage = BuildContextMessage(isDrawingMode, selectionInfo);
				if (selectionInfo.WasLimited)
				{
					try
					{
						JsonElement contextData = JsonSerializer.Deserialize<JsonElement>(collectedContextJson);
						var enhancedContext = new
						{
							partialContext = true,
							totalSelectedCount = selectionInfo.TotalCount,
							contextProvidedFor = 10,
							warningMessage = $"Context limited to first 10 of {selectionInfo.TotalCount} selected objects",
							contextType = (isDrawingMode ? "drawing" : "model"),
							context = contextData
						};
						return ToolExecutionResult.CreateSuccessResult(contextMessage, JsonSerializer.Serialize(enhancedContext));
					}
					catch
					{
						return ToolExecutionResult.CreateSuccessResult(contextMessage, collectedContextJson);
					}
				}
				return ToolExecutionResult.CreateSuccessResult(contextMessage, collectedContextJson);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("Error collecting Tekla context", ex.Message);
			}
		}

		private static bool IsInDrawingMode()
		{
			DrawingHandler drawingHandler = new DrawingHandler();
			if (drawingHandler.GetConnectionStatus())
			{
				Drawing activeDrawing = drawingHandler.GetActiveDrawing();
				return activeDrawing != null;
			}
			return false;
		}

		private static SelectionInfo GetSelectedModelObjects()
		{
			Tekla.Structures.Model.UI.ModelObjectSelector selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
			ModelObjectEnumerator selected = selector.GetSelectedObjects();
			int totalCount = selected.GetSize();
			if (totalCount <= 10)
			{
				return new SelectionInfo
				{
					TotalCount = totalCount,
					WasLimited = false,
					OriginalSelection = null
				};
			}
			ArrayList originalSelection = new ArrayList();
			ArrayList limitedSelection = new ArrayList();
			int count = 0;
			selected.Reset();
			while (selected.MoveNext())
			{
				Tekla.Structures.Model.ModelObject obj = selected.Current;
				if (obj != null)
				{
					originalSelection.Add(obj);
					if (count < 10)
					{
						limitedSelection.Add(obj);
						count++;
					}
				}
			}
			selector.Select(limitedSelection);
			return new SelectionInfo
			{
				TotalCount = totalCount,
				WasLimited = true,
				OriginalSelection = originalSelection
			};
		}

		private static SelectionInfo GetSelectedDrawingObjects()
		{
			DrawingHandler drawingHandler = new DrawingHandler();
			DrawingObjectSelector drawingSelector = drawingHandler.GetDrawingObjectSelector();
			DrawingObjectEnumerator selected = drawingSelector.GetSelected();
			int totalCount = 0;
			ArrayList originalSelection = new ArrayList();
			ArrayList limitedSelection = new ArrayList();
			while (selected.MoveNext())
			{
				DrawingObject obj = selected.Current;
				if (obj != null)
				{
					originalSelection.Add(obj);
					if (totalCount < 10)
					{
						limitedSelection.Add(obj);
					}
					totalCount++;
				}
			}
			if (totalCount <= 10)
			{
				return new SelectionInfo
				{
					TotalCount = totalCount,
					WasLimited = false,
					OriginalSelection = null
				};
			}
			drawingSelector.UnselectAllObjects();
			drawingSelector.SelectObjects(limitedSelection, false);
			return new SelectionInfo
			{
				TotalCount = totalCount,
				WasLimited = true,
				OriginalSelection = originalSelection
			};
		}

		private static void RestoreModelSelection(ArrayList originalSelection)
		{
			if (originalSelection != null && originalSelection.Count > 0)
			{
				Tekla.Structures.Model.UI.ModelObjectSelector selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
				selector.Select(originalSelection);
			}
		}

		private static void RestoreDrawingSelection(ArrayList originalSelection)
		{
			if (originalSelection != null && originalSelection.Count > 0)
			{
				DrawingHandler drawingHandler = new DrawingHandler();
				DrawingObjectSelector drawingSelector = drawingHandler.GetDrawingObjectSelector();
				drawingSelector.UnselectAllObjects();
				drawingSelector.SelectObjects(originalSelection, false);
			}
		}

		private static string BuildContextMessage(bool isDrawingMode, SelectionInfo selectionInfo)
		{
			string modeType = (isDrawingMode ? "Drawing" : "Model");
			if (!selectionInfo.WasLimited)
			{
				return modeType + " context collected successfully";
			}
			return $"Partial {modeType.ToLower()} context collected for first 10 of {selectionInfo.TotalCount} selected objects. " + "Full details are only provided for the first 10 objects to ensure performance.";
		}
	}
}
