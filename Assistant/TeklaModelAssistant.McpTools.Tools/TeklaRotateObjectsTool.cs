using System;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool for rotating objects in the Tekla Structures model around a specified axis.")]
	public class TeklaRotateObjectsTool
	{
		[Description("Rotates model objects around an axis defined by two points. The rotation axis is specified by two points (axisPoint1String and axisPoint2String in 'x,y,z' format). First, use TeklaPointPickerTool.PickPoints with two prompts to get the axis points from the user, then pass them to this tool. The angle is specified in degrees. Use either cachedSelectionId (from previous filter/query) or explicit elementIds to specify which objects to rotate.")]
		public static ToolExecutionResult RotateObjects([Description("Selection identifier referencing previously stored IDs of objects to rotate.")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to rotate.")] string elementIds, [Description("First point of the rotation axis in format 'x,y,z' (millimeters). Get from TeklaPointPickerTool.PickPoints.")] string axisPoint1String, [Description("Second point of the rotation axis in format 'x,y,z' (millimeters). Get from TeklaPointPickerTool.PickPoints.")] string axisPoint2String, [Description("Rotation angle in degrees. Positive values rotate counter-clockwise when looking along the axis direction (from point1 to point2).")] double angleDegrees, [Description("Opaque base64-encoded paging token (overrides offset/pageSize). Null by default.")] string cursor, [Description("The number of ids to process in one run (default 100)")] int pageSize, [Description("The offset to start retrieving items from (default 0)")] int offset, ISelectionCacheManager selectionCacheManager)
		{
			if (string.IsNullOrWhiteSpace(axisPoint1String))
			{
				return ToolExecutionResult.CreateErrorResult("axisPoint1String is required and cannot be empty. Use TeklaPointPickerTool.PickPoints to pick two points that define the rotation axis.");
			}
			if (string.IsNullOrWhiteSpace(axisPoint2String))
			{
				return ToolExecutionResult.CreateErrorResult("axisPoint2String is required and cannot be empty. Use TeklaPointPickerTool.PickPoints to pick two points that define the rotation axis.");
			}
			if (!axisPoint1String.TryParseToPoint(out var axisPoint1))
			{
				return ToolExecutionResult.CreateErrorResult("axisPoint1String '" + axisPoint1String + "' is invalid. Expected format: 'x,y,z'");
			}
			if (!axisPoint2String.TryParseToPoint(out var axisPoint2))
			{
				return ToolExecutionResult.CreateErrorResult("axisPoint2String '" + axisPoint2String + "' is invalid. Expected format: 'x,y,z'");
			}
			Vector axisVector = new Vector(axisPoint2.X - axisPoint1.X, axisPoint2.Y - axisPoint1.Y, axisPoint2.Z - axisPoint1.Z);
			if (axisVector.GetLength() < 1E-06)
			{
				return ToolExecutionResult.CreateErrorResult("The two axis points cannot be the same. Please provide two distinct points.");
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
				List<int> rotatedObjects = new List<int>();
				List<string> failedObjects = new List<string>();
				foreach (int id in idsToProcess)
				{
					ModelObject modelObject = model.SelectModelObject(new Identifier(id));
					if (modelObject != null)
					{
						try
						{
							if (ObjectTransformationHelper.Rotate(modelObject, axisPoint1, axisPoint2, angleRadians))
							{
								rotatedObjects.Add(id);
							}
							else
							{
								failedObjects.Add($"ID {id}: Rotate operation returned false");
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
				model.CommitChanges("(TMA) RotateObjects");
				string resultMessage = $"Successfully rotated {rotatedObjects.Count} object(s) by {angleDegrees}° around the specified axis.";
				if (failedObjects.Count > 0)
				{
					resultMessage += $" Failed to rotate {failedObjects.Count} object(s). Check data property for details.";
				}
				if (selectionResult.HasMore)
				{
					resultMessage += " More items available for processing.";
				}
				Dictionary<string, object> meta = ToolInputSelectionHandler.CreatePaginationMetadata(selectionResult, offset, pageSize);
				var resultData = new
				{
					rotatedCount = rotatedObjects.Count,
					failedCount = failedObjects.Count,
					rotatedIds = rotatedObjects,
					failures = ((failedObjects.Count > 0) ? failedObjects : null),
					rotationAxis = new
					{
						point1 = new
						{
							x = axisPoint1.X,
							y = axisPoint1.Y,
							z = axisPoint1.Z
						},
						point2 = new
						{
							x = axisPoint2.X,
							y = axisPoint2.Y,
							z = axisPoint2.Z
						}
					},
					angleDegrees = angleDegrees,
					angleRadians = angleRadians,
					meta = meta
				};
				return new ToolExecutionResult
				{
					Success = (rotatedObjects.Count > 0),
					Message = resultMessage,
					Data = resultData
				};
			}
			catch (Exception ex2)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while rotating objects.", ex2.Message);
			}
		}
	}
}
