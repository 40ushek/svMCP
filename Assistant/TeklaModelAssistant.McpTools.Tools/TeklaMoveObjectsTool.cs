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
	[Description("Tool for moving objects in the Tekla Structures model.")]
	public class TeklaMoveObjectsTool
	{
		[Description("Moves model objects by a specified translation vector. The translation can be specified in two ways: 1) Explicit X,Y,Z components (translationX, translationY, translationZ), or 2) Two points picked from the model (translationPoint1String and translationPoint2String in 'x,y,z' format). When using points, first call TeklaPointPickerTool.PickPoints with two prompts to get the points, then pass them to this tool. The translation vector will be calculated as (point2 - point1). Use either cachedSelectionId (from previous filter/query) or explicit elementIds to specify which objects to move.")]
		public static ToolExecutionResult MoveObjects([Description("Selection identifier referencing previously stored IDs of objects to move.")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to move.")] string elementIds, [Description("X component of the translation vector (millimeters). Use this OR translationPoint1String/translationPoint2String.")] double translationX, [Description("Y component of the translation vector (millimeters). Use this OR translationPoint1String/translationPoint2String.")] double translationY, [Description("Z component of the translation vector (millimeters). Use this OR translationPoint1String/translationPoint2String.")] double translationZ, [Description("First point defining the translation (start point) in format 'x,y,z' (millimeters). Get from TeklaPointPickerTool.PickPoints. Use this with translationPoint2String OR explicit X/Y/Z components.")] string translationPoint1String, [Description("Second point defining the translation (end point) in format 'x,y,z' (millimeters). Get from TeklaPointPickerTool.PickPoints. Use this with translationPoint1String OR explicit X/Y/Z components.")] string translationPoint2String, [Description("Opaque base64-encoded paging token (overrides offset/pageSize). Null by default.")] string cursor, [Description("The number of ids to process in one run (default 100)")] int pageSize, [Description("The offset to start retrieving items from (default 0)")] int offset, ISelectionCacheManager selectionCacheManager)
		{
			bool usingPoints = !string.IsNullOrWhiteSpace(translationPoint1String) || !string.IsNullOrWhiteSpace(translationPoint2String);
			bool usingExplicitVector = translationX != 0.0 || translationY != 0.0 || translationZ != 0.0;
			Vector translationVector;
			if (usingPoints)
			{
				if (string.IsNullOrWhiteSpace(translationPoint1String))
				{
					return ToolExecutionResult.CreateErrorResult("translationPoint1String is required when using point-based translation. Use TeklaPointPickerTool.PickPoints to pick two points.");
				}
				if (string.IsNullOrWhiteSpace(translationPoint2String))
				{
					return ToolExecutionResult.CreateErrorResult("translationPoint2String is required when using point-based translation. Use TeklaPointPickerTool.PickPoints to pick two points.");
				}
				if (!translationPoint1String.TryParseToPoint(out var translationPoint1))
				{
					return ToolExecutionResult.CreateErrorResult("translationPoint1String '" + translationPoint1String + "' is invalid. Expected format: 'x,y,z'");
				}
				if (!translationPoint2String.TryParseToPoint(out var translationPoint2))
				{
					return ToolExecutionResult.CreateErrorResult("translationPoint2String '" + translationPoint2String + "' is invalid. Expected format: 'x,y,z'");
				}
				translationVector = new Vector(translationPoint2.X - translationPoint1.X, translationPoint2.Y - translationPoint1.Y, translationPoint2.Z - translationPoint1.Z);
				if (translationVector.GetLength() < 1E-06)
				{
					return ToolExecutionResult.CreateErrorResult("The two translation points are the same or too close. Please provide two distinct points to define a translation vector.");
				}
			}
			else
			{
				if (!usingExplicitVector)
				{
					return ToolExecutionResult.CreateErrorResult("Please specify translation either by providing X/Y/Z components OR by providing two points (translationPoint1String and translationPoint2String).");
				}
				translationVector = new Vector(translationX, translationY, translationZ);
			}
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
				List<int> movedObjects = new List<int>();
				List<string> failedObjects = new List<string>();
				foreach (int id in idsToProcess)
				{
					ModelObject modelObject = model.SelectModelObject(new Identifier(id));
					if (modelObject != null)
					{
						try
						{
							if (ObjectTransformationHelper.Move(modelObject, translationVector))
							{
								movedObjects.Add(id);
							}
							else
							{
								failedObjects.Add($"ID {id}: Move operation returned false");
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
				model.CommitChanges("(TMA) MoveObjects");
				string resultMessage = $"Successfully moved {movedObjects.Count} object(s).";
				if (failedObjects.Count > 0)
				{
					resultMessage += $" Failed to move {failedObjects.Count} object(s). Check data property for details.";
				}
				if (selectionResult.HasMore)
				{
					resultMessage += " More items available for processing.";
				}
				Dictionary<string, object> meta = ToolInputSelectionHandler.CreatePaginationMetadata(selectionResult, offset, pageSize);
				object translationInfo;
				if (usingPoints)
				{
					translationPoint1String.TryParseToPoint(out var point1);
					translationPoint2String.TryParseToPoint(out var point2);
					translationInfo = new
					{
						method = "points",
						vector = new
						{
							x = translationVector.X,
							y = translationVector.Y,
							z = translationVector.Z
						},
						point1 = new
						{
							x = point1.X,
							y = point1.Y,
							z = point1.Z
						},
						point2 = new
						{
							x = point2.X,
							y = point2.Y,
							z = point2.Z
						}
					};
				}
				else
				{
					translationInfo = new
					{
						method = "explicit",
						vector = new
						{
							x = translationVector.X,
							y = translationVector.Y,
							z = translationVector.Z
						}
					};
				}
				var resultData = new
				{
					movedCount = movedObjects.Count,
					failedCount = failedObjects.Count,
					movedIds = movedObjects,
					failures = ((failedObjects.Count > 0) ? failedObjects : null),
					translation = translationInfo,
					meta = meta
				};
				return new ToolExecutionResult
				{
					Success = (movedObjects.Count > 0),
					Message = resultMessage,
					Data = resultData
				};
			}
			catch (Exception ex2)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while moving objects.", ex2.Message);
			}
		}
	}
}
