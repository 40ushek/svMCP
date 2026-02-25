using System;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool for mirroring objects in the Tekla Structures model across a mirror line on the current work plane.")]
	public class TeklaMirrorObjectsTool
	{
		[Description("Mirrors model objects across a line defined by a point and an angle on the current work plane. The mirror line is specified by a point (mirrorLinePointString in 'x,y,z' format, only x and y are used) and an angle to the x-axis. First, use TeklaPointPickerTool.PickPoints with one prompt to get the mirror line point from the user, then pass it to this tool. The angle is specified in degrees and defines the orientation of the mirror line relative to the x-axis of the current work plane. Use either cachedSelectionId (from previous filter/query) or explicit elementIds to specify which objects to mirror.")]
		public static ToolExecutionResult MirrorObjects([Description("Selection identifier referencing previously stored IDs of objects to mirror.")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to mirror.")] string elementIds, [Description("Point on the mirror line in format 'x,y,z' (millimeters). Get from TeklaPointPickerTool.PickPoints. Only x and y coordinates are used.")] string mirrorLinePointString, [Description("Angle of the mirror line to the x-axis in degrees. 0° means horizontal line, 90° means vertical line.")] double angleDegrees, [Description("Opaque base64-encoded paging token (overrides offset/pageSize). Null by default.")] string cursor, [Description("The number of ids to process in one run (default 100)")] int pageSize, [Description("The number of ids to skip (default 0)")] int offset, ISelectionCacheManager selectionCacheManager)
		{
			if (string.IsNullOrWhiteSpace(mirrorLinePointString))
			{
				return ToolExecutionResult.CreateErrorResult("mirrorLinePointString is required and cannot be empty. Use TeklaPointPickerTool.PickPoints to pick a point on the mirror line.");
			}
			if (!mirrorLinePointString.TryParseToPoint(out var mirrorLinePoint))
			{
				return ToolExecutionResult.CreateErrorResult("mirrorLinePointString '" + mirrorLinePointString + "' is invalid. Expected format: 'x,y,z'");
			}
			double angleRadians = angleDegrees * Math.PI / 180.0;
			Model model = new Model();
			if (!model.GetConnectionStatus())
			{
				return ToolExecutionResult.CreateErrorResult("No model is open.");
			}
			SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, cursor, pageSize, offset, selectionCacheManager);
			if (!selectionResult.Success)
			{
				return new ToolExecutionResult
				{
					Success = false,
					Message = selectionResult.Message,
					Data = selectionResult.Data,
					Error = selectionResult.Message
				};
			}
			try
			{
				IList<int> idsToProcess = selectionResult.Ids;
				List<int> mirroredObjects = new List<int>();
				List<string> failedObjects = new List<string>();
				foreach (int id in idsToProcess)
				{
					ModelObject modelObject = model.SelectModelObject(new Identifier(id));
					if (modelObject != null)
					{
						try
						{
							if (ObjectTransformationHelper.Mirror(modelObject, mirrorLinePoint.X, mirrorLinePoint.Y, angleRadians))
							{
								mirroredObjects.Add(id);
							}
							else
							{
								failedObjects.Add($"ID {id}: Mirror operation returned false");
							}
						}
						catch (Exception ex)
						{
							failedObjects.Add($"ID {id}: {ex.Message}");
						}
					}
					else
					{
						failedObjects.Add($"ID {id}: Object not found or is not a ModelObject");
					}
				}
				model.CommitChanges("Mirror objects");
				string resultMessage = $"Successfully mirrored {mirroredObjects.Count} object(s) across the mirror line.";
				if (failedObjects.Count > 0)
				{
					resultMessage += $" Failed to mirror {failedObjects.Count} object(s). Check data property for details.";
				}
				if (selectionResult.HasMore)
				{
					resultMessage += " More items available for processing.";
				}
				Dictionary<string, object> meta = ToolInputSelectionHandler.CreatePaginationMetadata(selectionResult, offset, pageSize);
				var resultData = new
				{
					mirroredCount = mirroredObjects.Count,
					failedCount = failedObjects.Count,
					mirroredIds = mirroredObjects,
					failures = ((failedObjects.Count > 0) ? failedObjects : null),
					mirrorLine = new
					{
						point = new
						{
							x = mirrorLinePoint.X,
							y = mirrorLinePoint.Y,
							z = mirrorLinePoint.Z
						},
						angleDegrees = angleDegrees,
						angleRadians = angleRadians
					},
					meta = meta
				};
				return new ToolExecutionResult
				{
					Success = (mirroredObjects.Count > 0),
					Message = resultMessage,
					Data = resultData
				};
			}
			catch (Exception ex2)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while mirroring objects.", ex2.Message);
			}
		}
	}
}
