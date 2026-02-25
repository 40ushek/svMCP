using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tools for context and operations")]
	public class TeklaTools
	{
		[Description("Set visual properties (colors, line types, fills) on Tekla objects.\r\n\r\nPROPERTY MAPPINGS:\r\n- 'hiddenLineColor' → Hidden line color\r\n- 'visibleLineColor' → Visible line color  \r\n- 'sectionLineColor' → Section line color\r\n- 'referenceLineColor' → Reference line color\r\n\r\nTEKLA DRAWING COLORS (use these exact values):\r\n- 152 = Invisible (light gray, screen only)\r\n- 153 = Black\r\n- 154 = Brown (NewLine1)\r\n- 155 = Green (NewLine2)\r\n- 156 = Dark Blue (NewLine3) \r\n- 157 = Forest Green (NewLine4)\r\n- 158 = Orange (NewLine5)\r\n- 159 = Gray (NewLine6)\r\n- 160 = Red\r\n- 161 = Bright Green\r\n- 162 = Blue\r\n- 163 = Cyan (Turquoise)\r\n- 164 = Yellow\r\n- 165 = Magenta\r\n\r\nLINE TYPES: 1=solid, 2=dashed, 3=dotted, 4=dash-dot, 5=dash-dot-dot")]
		public static async Task<object> SetObjectProperties([Description("Comma-separated object IDs to modify (e.g., '100,200,300')")] string objectIds, [Description("Property name to set (see description for available properties)")] string propertyName, [Description("Property value to set (see description for valid values)")] string propertyValue)
		{
			try
			{
				await System.Threading.Tasks.Task.CompletedTask;
				List<int> idList = ParseObjectIds(objectIds);
				if (idList.Count == 0)
				{
					return new
					{
						success = false,
						error = "No valid object IDs provided",
						message = "Please provide comma-separated object IDs (e.g., '100,200,300')"
					};
				}
				if (!int.TryParse(propertyValue, out var propValue))
				{
					return new
					{
						success = false,
						error = "Invalid property value",
						message = "Property value must be a number"
					};
				}
				if (propertyName.Contains("Color") && !IsValidDrawingColor(propValue))
				{
					return new
					{
						success = false,
						error = "Invalid color value",
						message = $"Color value must be 130-165. You provided {propValue}. Use 'List all drawing colors' to see valid values."
					};
				}
				int modifiedCount = ApplyProperties(idList, propertyName, propValue);
				string colorName = (propertyName.Contains("Color") ? GetColorName(propValue) : propValue.ToString());
				return new
				{
					success = (modifiedCount > 0),
					objectsModified = modifiedCount,
					totalObjects = idList.Count,
					propertySet = new
					{
						name = propertyName,
						value = propValue,
						colorName = (propertyName.Contains("Color") ? colorName : null)
					},
					modifiedObjectIds = idList.Take(modifiedCount).ToList(),
					message = ((modifiedCount > 0) ? $"Successfully set {propertyName} to {colorName} (value {propValue}) on {modifiedCount} objects" : "No objects were modified"),
					timestamp = DateTime.Now
				};
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				return new
				{
					success = false,
					error = ex2.Message,
					message = "Failed to set object properties"
				};
			}
		}

		[Description("List all available Tekla DrawingColors with their numeric values")]
		public static async Task<object> ListDrawingColors()
		{
			try
			{
				await System.Threading.Tasks.Task.CompletedTask;
				List<object> colors = new List<object>();
				Array enumValues = Enum.GetValues(typeof(DrawingColors));
				foreach (DrawingColors color in enumValues)
				{
					colors.Add(new
					{
						value = (int)color,
						name = color.ToString(),
						description = GetColorDescription(color)
					});
				}
				return new
				{
					success = true,
					availableColors = colors,
					totalColors = colors.Count,
					message = "Use the 'value' numbers when setting colors"
				};
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				return new
				{
					success = false,
					error = ex2.Message,
					message = "Failed to list drawing colors"
				};
			}
		}

		private static bool IsValidDrawingColor(int colorValue)
		{
			return colorValue >= 130 && colorValue <= 165;
		}

		private static string GetColorName(int colorValue)
		{
			try
			{
				DrawingColors enumValue = (DrawingColors)colorValue;
				return enumValue.ToString();
			}
			catch
			{
				return $"Invalid({colorValue})";
			}
		}

		private static string GetColorDescription(DrawingColors color)
		{
			if (1 == 0)
			{
			}
			string result;
			switch (color)
			{
			case DrawingColors.Invisible:
				result = "Light gray (screen only)";
				break;
			case DrawingColors.Black:
				result = "Black";
				break;
			case DrawingColors.Red:
				result = "Red";
				break;
			case DrawingColors.Green:
				result = "Bright Green";
				break;
			case DrawingColors.Blue:
				result = "Blue";
				break;
			case DrawingColors.Cyan:
				result = "Cyan/Turquoise";
				break;
			case DrawingColors.Yellow:
				result = "Yellow";
				break;
			case DrawingColors.Magenta:
				result = "Magenta";
				break;
			default:
				result = color.ToString();
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}

		private static List<int> ParseObjectIds(string objectIds)
		{
			List<int> idList = new List<int>();
			if (!string.IsNullOrEmpty(objectIds))
			{
				string[] array = objectIds.Split(',');
				foreach (string idStr in array)
				{
					if (int.TryParse(idStr.Trim(), out var id))
					{
						idList.Add(id);
					}
				}
			}
			return idList;
		}

		private static int ApplyProperties(List<int> objectIds, string propertyName, int propertyValue)
		{
			int modifiedCount = 0;
			try
			{
				DrawingHandler drawingHandler = new DrawingHandler();
				if (drawingHandler.GetConnectionStatus())
				{
					modifiedCount = ApplyDrawingProperties(objectIds, propertyName, propertyValue);
				}
				else
				{
					Model model = new Model();
					if (model.GetConnectionStatus())
					{
						modifiedCount = ApplyModelProperties(objectIds, propertyName, propertyValue);
					}
				}
			}
			catch
			{
			}
			return modifiedCount;
		}

		private static int ApplyDrawingProperties(List<int> objectIds, string propertyName, int propertyValue)
		{
			int modifiedCount = 0;
			try
			{
				DrawingHandler drawingHandler = new DrawingHandler();
				DrawingObjectSelector selector = drawingHandler.GetDrawingObjectSelector();
				DrawingObjectEnumerator selectedObjects = selector.GetSelected();
				while (selectedObjects.MoveNext())
				{
					if (!(selectedObjects.Current is Tekla.Structures.Drawing.Part part))
					{
						continue;
					}
					try
					{
						int id = part.GetIdentifier().ID;
						if (!objectIds.Contains(id))
						{
							continue;
						}
						Tekla.Structures.Drawing.Part.PartAttributes attributes = part.Attributes;
						if (attributes == null)
						{
							attributes = new Tekla.Structures.Drawing.Part.PartAttributes();
						}
						bool modified = false;
						switch (propertyName)
						{
						case "sectionLineColor":
							if (attributes.SectionLines == null)
							{
								attributes.SectionLines = new LineTypeAttributes();
							}
							attributes.SectionLines.Color = (DrawingColors)propertyValue;
							modified = true;
							break;
						case "visibleLineColor":
							if (attributes.VisibleLines == null)
							{
								attributes.VisibleLines = new LineTypeAttributes();
							}
							attributes.VisibleLines.Color = (DrawingColors)propertyValue;
							modified = true;
							break;
						case "hiddenLineColor":
							if (attributes.HiddenLines == null)
							{
								attributes.HiddenLines = new LineTypeAttributes();
							}
							attributes.HiddenLines.Color = (DrawingColors)propertyValue;
							modified = true;
							break;
						}
						if (modified)
						{
							part.Attributes = attributes;
							part.Modify();
							modifiedCount++;
						}
					}
					catch
					{
					}
				}
				drawingHandler.GetActiveDrawing()?.CommitChanges("(TMA) SetObjectProperties");
			}
			catch
			{
			}
			return modifiedCount;
		}

		private static int ApplyModelProperties(List<int> objectIds, string propertyName, int propertyValue)
		{
			int modifiedCount = 0;
			try
			{
				Model model = new Model();
				foreach (int objectId in objectIds)
				{
					try
					{
						Tekla.Structures.Model.ModelObject modelObject = model.SelectModelObject(new Identifier(objectId));
						if (modelObject != null)
						{
							modelObject.SetUserProperty(propertyName.ToUpper(), propertyValue);
							modelObject.Modify();
							modifiedCount++;
						}
					}
					catch
					{
					}
				}
				model.CommitChanges("(TMA) SetObjectProperties");
			}
			catch
			{
			}
			return modifiedCount;
		}
	}
}
