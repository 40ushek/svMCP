using System;
using System.ComponentModel;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool for controlling view depth settings (visibility above and below the view plane) in Tekla Structures.")]
	public class TeklaViewDepthTool
	{
		[Description("Set the view depth (visibility above and below the view plane) for a view. View depth controls how much above and below the view plane elements are visible.")]
		public static ToolExecutionResult SetViewDepth([Description("The view depth up (visibility above the view plane). Example: 1000 means objects up to 1000 units above the view plane will be visible.")] double? viewDepthUp = null, [Description("The view depth down (visibility below the view plane). Example: 500 means objects up to 500 units below the view plane will be visible.")] double? viewDepthDown = null, [Description("The name of the view to modify. If not provided, the active view will be modified.")] string viewName = null)
		{
			try
			{
				var (targetView, error) = GetTargetView(viewName);
				if (error != null)
				{
					return error;
				}
				double originalUp = targetView.ViewDepthUp;
				double originalDown = targetView.ViewDepthDown;
				if (!viewDepthUp.HasValue && !viewDepthDown.HasValue)
				{
					return ToolExecutionResult.CreateSuccessResult("No changes requested. Current view depth for '" + targetView.Name + "': " + $"Up = {originalUp}, Down = {originalDown}");
				}
				if (viewDepthUp.HasValue)
				{
					targetView.ViewDepthUp = viewDepthUp.Value;
				}
				if (viewDepthDown.HasValue)
				{
					targetView.ViewDepthDown = viewDepthDown.Value;
				}
				if (!targetView.Modify())
				{
					return ToolExecutionResult.CreateErrorResult("Failed to modify view depth settings.");
				}
				string changes = "";
				if (viewDepthUp.HasValue)
				{
					changes = $"Up: {originalUp} → {viewDepthUp.Value}";
				}
				if (viewDepthDown.HasValue)
				{
					if (changes.Length > 0)
					{
						changes += ", ";
					}
					changes += $"Down: {originalDown} → {viewDepthDown.Value}";
				}
				return ToolExecutionResult.CreateSuccessResult("View depth updated for '" + targetView.Name + "'. " + changes);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while setting view depth.", ex.Message);
			}
		}

		private static (View view, ToolExecutionResult error) GetTargetView(string viewName = null)
		{
			View targetView = null;
			if (!string.IsNullOrWhiteSpace(viewName))
			{
				ModelViewEnumerator viewEnum = ViewHandler.GetAllViews();
				while (viewEnum.MoveNext())
				{
					if (viewEnum.Current.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase))
					{
						targetView = viewEnum.Current;
						break;
					}
				}
				if (targetView == null)
				{
					return (view: null, error: ToolExecutionResult.CreateErrorResult("View '" + viewName + "' not found."));
				}
			}
			else
			{
				targetView = ViewHandler.GetActiveView();
				if (targetView == null)
				{
					return (view: null, error: ToolExecutionResult.CreateErrorResult("No active view found."));
				}
			}
			return (view: targetView, error: null);
		}
	}
}
