using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for customizing annotations in Tekla drawings.")]
	public class TeklaSetMarkContentTool
	{
		private const double FontHeightTolerance = 0.01;

		[Description("IMPORTANT: Modifies EXISTING part marks. Can change content AND/OR appearance (font, color, height). If contentElements is empty, only appearance is changed.")]
		public static ToolExecutionResult SetMarkContent([Description("Optional: A comma-separated list of attributes to display (e.g., 'PART_POS,PROFILE'). If empty, existing content is kept, and only appearance is changed.")] string contentElements, [Description("Selection identifier referencing previously stored IDs")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query")] string elementIds, [Description("Opaque base64-encoded paging token (overrides offset/pageSize)")] string cursor, [Description("The number of ids to process in one run (default 100)")] int pageSize, [Description("The offset to start retrieving items from (default 0)")] int offset, ISelectionCacheManager selectionCacheManager, [Description("Optional: The font name for the mark content (e.g., 'Arial').")] string fontName = null, [Description("Optional: The font color for the mark content (e.g., 'Red', 'Blue').")] string fontColor = null, [Description("Optional: The font height for the mark content. Must be > 0.")] double? fontHeight = null)
		{
			DrawingHandler drawingHandler = new DrawingHandler();
			Drawing activeDrawing = drawingHandler.GetActiveDrawing();
			if (activeDrawing == null)
			{
				return ToolExecutionResult.CreateErrorResult("No drawing is currently open.");
			}
			if (string.IsNullOrWhiteSpace(contentElements) && string.IsNullOrWhiteSpace(fontName) && string.IsNullOrWhiteSpace(fontColor) && fontHeight <= 0.0)
			{
				return ToolExecutionResult.CreateErrorResult("No changes requested. Please provide contentElements or at least one font attribute (fontName, fontColor, fontHeight).");
			}
			List<string> newAttributes = null;
			bool updateContent = !string.IsNullOrWhiteSpace(contentElements);
			if (updateContent)
			{
				newAttributes = (from a in contentElements.Split(',')
					select a.Trim()).ToList();
			}
			SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(drawingHandler, cachedSelectionId, useCurrentSelectionString, elementIds, cursor, pageSize, offset, selectionCacheManager);
			DrawingObjectEnumerator drawingObjects = drawingHandler.GetActiveDrawing().GetSheet().GetAllObjects();
			List<int> updatedObjectIds = new List<int>();
			Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>();
			while (drawingObjects.MoveNext())
			{
				DrawingObject currentObject = drawingObjects.Current;
				int id = currentObject.GetIdentifier().ID;
				string idString = id.ToString();
				if ((!selectionResult.Ids.Contains(id) && (!(currentObject is ModelObject drawingModelObject) || !selectionResult.Ids.Contains(drawingModelObject.ModelIdentifier.ID))) || !(currentObject is Mark mark))
				{
					continue;
				}
				try
				{
					ContainerElement contentContainer = mark.Attributes.Content;
					IEnumerator contentEnumerator = contentContainer.GetEnumerator();
					FontAttributes existingFont = (contentEnumerator.MoveNext() ? (contentEnumerator.Current as PropertyElement) : null)?.Font;
					FontAttributes newFont = new FontAttributes();
					if (existingFont != null)
					{
						newFont = (FontAttributes)existingFont.Clone();
					}
					bool fontChanged = false;
					if (!string.IsNullOrWhiteSpace(fontName) && newFont.Name != fontName)
					{
						newFont.Name = fontName;
						fontChanged = true;
					}
					if (fontHeight.HasValue && fontHeight.Value > 0.0 && Math.Abs(newFont.Height - fontHeight.Value) > 0.01)
					{
						newFont.Height = fontHeight.Value;
						fontChanged = true;
					}
					if (!string.IsNullOrWhiteSpace(fontColor) && TryParseDrawingColor(fontColor, out var color) && newFont.Color != color)
					{
						newFont.Color = color;
						fontChanged = true;
					}
					if (updateContent)
					{
						contentContainer.Clear();
						foreach (string attribute in newAttributes)
						{
							PropertyElement propertyElement = CreatePropertyElementFromString(attribute);
							if (propertyElement != null)
							{
								propertyElement.Font = newFont;
								contentContainer.Add(propertyElement);
							}
							else
							{
								AddError(errors, idString, "Could not create content element for '" + attribute + "'. Skipping.");
							}
						}
						goto IL_03b4;
					}
					if (!fontChanged)
					{
						continue;
					}
					IEnumerator existingElementsEnumerator = contentContainer.GetEnumerator();
					while (existingElementsEnumerator.MoveNext())
					{
						if (existingElementsEnumerator.Current is PropertyElement existingPropElement)
						{
							FontAttributes updatedFont = (FontAttributes)newFont.Clone();
							existingPropElement.Font = updatedFont;
						}
					}
					goto IL_03b4;
					IL_03b4:
					if (mark.Modify())
					{
						updatedObjectIds.Add(id);
					}
					else
					{
						AddError(errors, idString, "Failed to modify the mark.");
					}
				}
				catch (Exception ex)
				{
					AddError(errors, idString, "An unexpected error occurred: " + ex.Message);
				}
			}
			if (updatedObjectIds.Count == 0 && errors.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult("No matching part marks found or no changes were applied to the found marks.");
			}
			activeDrawing.CommitChanges("(TMA) SetMarkContent");
			Dictionary<string, object> meta = ToolInputSelectionHandler.CreatePaginationMetadata(selectionResult, offset, pageSize);
			List<Dictionary<string, object>> items = updatedObjectIds.Select((int num) => new Dictionary<string, object> { { "id", num } }).ToList();
			Dictionary<string, object> resultData = new Dictionary<string, object>
			{
				{ "items", items },
				{ "meta", meta },
				{ "updatedObjectIds", updatedObjectIds },
				{ "errors", errors }
			};
			StringBuilder summary = new StringBuilder();
			summary.AppendLine($"Operation completed. Successfully updated {updatedObjectIds.Count} objects.");
			summary.AppendLine($"Processed {items.Count} items from {selectionResult.Total} total (offset {offset}, pageSize {pageSize}).");
			if (selectionResult.HasMore)
			{
				summary.AppendLine("More items available. Use nextCursor or nextOffset for the next page.");
			}
			if (errors.Count > 0)
			{
				summary.AppendLine($"Encountered {errors.Values.Sum((List<string> v) => v.Count)} errors on {errors.Count} objects.");
			}
			return ToolExecutionResult.CreateSuccessResult(summary.ToString().Trim(), resultData);
		}

		private static void AddError(Dictionary<string, List<string>> errorLog, string id, string message)
		{
			if (!errorLog.ContainsKey(id))
			{
				errorLog[id] = new List<string>();
			}
			errorLog[id].Add(message);
		}

		private static PropertyElement CreatePropertyElementFromString(string attributeName)
		{
			switch (attributeName.ToUpper())
			{
			case "PART_POS":
			case "PARTPOSITION":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition());
			case "PROFILE":
			case "PART_PROFILE":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile());
			case "MATERIAL":
			case "PART_MATERIAL":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material());
			case "ASSEMBLY_POS":
			case "PART_PREFIX":
			case "ASSEMBLYPOSITION":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition());
			case "NAME":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name());
			case "CLASS":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class());
			case "SIZE":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size());
			case "CAMBER":
				return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber());
			default:
				return null;
			}
		}

		private static bool TryParseDrawingColor(string colorName, out DrawingColors color)
		{
			return Enum.TryParse<DrawingColors>(colorName, true, out color);
		}
	}
}
