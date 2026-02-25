using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to find drawings")]
	public class TeklaDrawingsFilterTool
	{
		[Description("Find drawings by multiple properties. Accepts a JSON array of property filters, e.g. [{ \"property\": \"Name\", \"value\": \"GA Drawing 1\" }, { \"property\": \"Mark\", \"value\": \"100\" }]")]
		public static ToolExecutionResult FindDrawingsByProperties([Description("A JSON array of property filters. Each filter is an object with 'property' and 'value'. Supported properties: Name, Type, Mark, Status, etc.")] string drawingPropertyFiltersString)
		{
			try
			{
				if (!drawingPropertyFiltersString.TryConvertFromJson<List<Dictionary<string, string>>>(out var filters))
				{
					return ToolExecutionResult.CreateErrorResult("The 'drawingPropertyFiltersString' argument must be a JSON array of property filters.");
				}
				DrawingHandler drawingHandler = new DrawingHandler();
				DrawingEnumerator drawings = drawingHandler.GetDrawings();
				List<Drawing> foundDrawings = new List<Drawing>();
				while (drawings.MoveNext())
				{
					Drawing drawing = drawings.Current;
					bool matchesAll = true;
					foreach (Dictionary<string, string> filter in filters)
					{
						if (!filter.TryGetValue("property", out var property) || !filter.TryGetValue("value", out var value))
						{
							matchesAll = false;
							break;
						}
						string text = property.Trim().ToLowerInvariant();
						string text2 = text;
						if (!(text2 == "name"))
						{
							if (text2 == "mark")
							{
								if (!string.Equals(drawing.Mark, value, StringComparison.OrdinalIgnoreCase))
								{
									matchesAll = false;
								}
							}
							else
							{
								matchesAll = false;
							}
						}
						else if (!string.Equals(drawing.Name, value, StringComparison.OrdinalIgnoreCase))
						{
							matchesAll = false;
						}
						if (!matchesAll)
						{
							break;
						}
					}
					if (matchesAll)
					{
						foundDrawings.Add(drawing);
					}
				}
				if (foundDrawings.Any())
				{
					return ToolExecutionResult.CreateSuccessResult($"Found {foundDrawings.Count} drawings matching all filters.", foundDrawings.Select((Drawing d) => d.GetIdentifier().GUID.ToString()).ToList());
				}
				return ToolExecutionResult.CreateErrorResult("No drawings found matching the given filters.");
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("Error while trying to find drawings.", ex.Message);
			}
		}
	}
}
