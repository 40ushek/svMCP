using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Generic Tekla Structures tool to pick one or multiple points.")]
	public class TeklaPointPickerTool
	{
		[Description("Starts an interactive routine for the user to pick points in the model.Returns the picked points as strings. Requires active view.")]
		public static async Task<ToolExecutionResult> PickPoints([Description("JSON list of messages to be used when picking each point. Controls also the number of points based on the number of messages. Example:[\"Pick base point.\", \"Pick north direction\"]")] string messageListString)
		{
			if (!messageListString.TryConvertFromJson<List<string>>(out var messageList))
			{
				return ToolExecutionResult.CreateErrorResult("The 'messageListString' argument is required and must be a valid JSON list of strings.");
			}
			try
			{
				List<string> pointsList = new List<string>();
				await Task.Run(delegate
				{
					foreach (string current in messageList)
					{
						Point point = null;
						Picker picker = new Picker();
						point = picker.PickPoint(current);
						pointsList.Add(point.ConvertToString());
					}
				});
				return ToolExecutionResult.CreateSuccessResult($"{pointsList.Count} points picked successfully.", pointsList);
			}
			catch (ApplicationException ex)
			{
				return ToolExecutionResult.CreateErrorResult("Point picking was cancelled by the user.", ex.Message);
			}
			catch (Exception ex2)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while picking points.", ex2.Message);
			}
		}
	}
}
