using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to create a base point.")]
	public class TeklaCreateBasePointTool
	{
		private static readonly Regex InvalidCharsRegex = new Regex("[^a-zA-Z0-9_ -]", RegexOptions.Compiled);

		[Description("Creates a new base point in the Tekla Structures model using provided coordinates and settings. The origin and north direction must be specified as points in the format 'x,y,z'.")]
		public static ToolExecutionResult CreateBasePoint([Description("The name to assign to the new base point. Example: 'Project_Origin'")] string basePointName, [Description("Set this to 'true' to make this the new project base point. Defaults to 'false'.")] string setAsProjectBasePointString, [Description("Origin point for the base point in format 'x,y,z'.")] string originPointString, [Description("North direction point in format 'x,y,z'.")] string northDirectionPointString)
		{
			if (string.IsNullOrWhiteSpace(basePointName))
			{
				return ToolExecutionResult.CreateErrorResult("The 'basePointName' argument is required and cannot be empty.");
			}
			if (InvalidCharsRegex.IsMatch(basePointName))
			{
				return ToolExecutionResult.CreateErrorResult("The 'basePointName' contains invalid characters. Only letters, numbers, spaces, hyphens, and underscores are allowed.");
			}
			if (!setAsProjectBasePointString.TryConvertFromJson(out var setAsProjectBasePoint))
			{
				return ToolExecutionResult.CreateErrorResult("The 'setAsProjectBasePointString' argument must be a boolean value (true or false).");
			}
			if (!originPointString.TryParseToPoint(out var basePointLocation))
			{
				return ToolExecutionResult.CreateErrorResult("The 'originPointString' argument must be in the format 'x,y,z'. ");
			}
			if (!northDirectionPointString.TryParseToPoint(out var orientationPoint))
			{
				return ToolExecutionResult.CreateErrorResult("The 'northDirectionPointString' argument must be in the format 'x,y,z'.");
			}
			try
			{
				orientationPoint.Z = basePointLocation.Z;
				Vector axisY = new Vector(orientationPoint - basePointLocation);
				if (axisY.GetLength() < 1E-06)
				{
					return ToolExecutionResult.CreateErrorResult("The two points cannot be the same. The direction could not be determined.");
				}
				axisY.Normalize();
				Vector globalZ = new Vector(0.0, 0.0, 1.0);
				Vector axisX = axisY.Cross(globalZ);
				axisX.Normalize();
				double angleToNorth = 0.0 - Math.Atan2(axisY.X, axisY.Y);
				Model model = new Model();
				BasePoint newBasePoint = new BasePoint(basePointName)
				{
					LocationInModelX = basePointLocation.X,
					LocationInModelY = basePointLocation.Y,
					LocationInModelZ = basePointLocation.Z,
					AngleToNorth = angleToNorth,
					IsProjectBasePoint = setAsProjectBasePoint
				};
				if (newBasePoint.Insert())
				{
					model.CommitChanges("(TMA) CreateBasePoint");
					return ToolExecutionResult.CreateSuccessResult("Base point '" + basePointName + "' created successfully.", newBasePoint.Guid);
				}
				return ToolExecutionResult.CreateErrorResult("Failed to insert the new base point into the model.");
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while creating the base point.", ex.Message);
			}
		}
	}
}
