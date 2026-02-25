using System;
using System.ComponentModel;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool for creating views in model.")]
	public class TeklaCreateViewTool
	{
		[Description("Create a view in the model at the specified location")]
		public static ToolExecutionResult CreateView([Description("The name of the view")] string viewName, [Description("Defines the horizontal direction vector of the view's coordinate system. For a view on the XY plane, set this to '1, 0, 0'. For a view on the XZ plane, set this to '0, 0, 1'. For a view on the ZY plane, set this to '0, -1, 0'. This vector establishes the horizontal axis in the view's plane.")] string xAxisVectorString, [Description("Defines the horizontal direction vector perpendicular to AxisX within the view's coordinate system. For a view on the XY plane, set this to '0, 1, 0'. For a view on the XZ plane, set this to '0, 1, 0'. For a view on the ZY plane, set this to '0, 0, 1'. This vector establishes the vertical axis in the view's plane.")] string yAxisVectorString, [Description("Specifies the origin point of the view's coordinate system in the model space. For a view on the XY plane, set this to '0, 0, value'. For a view on the XZ plane, set this to '0, value, 0'. For a view on the ZY plane, set this to 'value, 0, 0'. This point determines where the view is centered in Tekla Structures.")] string originPointString, [Description("The view depth up. Default is 2000")] double depthUp = 2000.0, [Description("The view depth down. Default is 2000")] double depthDown = 2000.0)
		{
			if (string.IsNullOrWhiteSpace(viewName))
			{
				return ToolExecutionResult.CreateErrorResult("The 'viewName' argument is required and cannot be empty.");
			}
			if (!xAxisVectorString.TryParseToVector(out var axisXVector))
			{
				return ToolExecutionResult.CreateErrorResult("The 'xAxisVectorString' argument is required and must be in the format 'x,y,z'.");
			}
			if (!yAxisVectorString.TryParseToVector(out var axisYVector))
			{
				return ToolExecutionResult.CreateErrorResult("The 'yAxisVectorString' argument is required and must be in the format 'x,y,z'.");
			}
			if (!originPointString.TryParseToPoint(out var originPoint))
			{
				return ToolExecutionResult.CreateErrorResult("The 'originPointString' argument is required and must be in the format 'x,y,z'.");
			}
			try
			{
				CoordinateSystem viewCoordinateSystem = new CoordinateSystem(originPoint, axisXVector, axisYVector);
				View currentView = ViewHandler.GetActiveView();
				View view = new View
				{
					Name = viewName,
					DisplayCoordinateSystem = currentView.DisplayCoordinateSystem,
					DisplayType = currentView.DisplayType,
					CurrentRepresentation = currentView.CurrentRepresentation,
					ViewCoordinateSystem = viewCoordinateSystem,
					ViewDepthUp = depthUp,
					ViewDepthDown = depthDown,
					ViewFilter = currentView.ViewFilter,
					ViewProjection = currentView.ViewProjection,
					VisibilitySettings = currentView.VisibilitySettings,
					WorkArea = currentView.WorkArea
				};
				view.ViewCoordinateSystem.Origin = originPoint;
				if (view.Insert())
				{
					ViewHandler.ShowView(view);
					view.Modify();
					return ToolExecutionResult.CreateSuccessResult("View '" + view.Name + "' created successfully.");
				}
				return ToolExecutionResult.CreateErrorResult("Failed to insert the view into the model.");
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while creating the view.", ex.Message);
			}
		}
	}
}
