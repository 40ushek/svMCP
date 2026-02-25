using System;
using System.ComponentModel;
using Tekla.Structures.ModelInternal;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Various tools for Tekla.")]
	public class TeklaDotStartActionTool
	{
		[Description("Execute a Tekla Structures action using dotStartAction API. List of actions is in TeklaActions.json. Do not allow other actions.")]
		public static ToolExecutionResult DotStartAction([Description("Name of the action from the list from TeklaActions.json")] string actionName, [Description("Parameter string based on the definition from TeklaActions.json. Only use parameters that are explicitly specified in the document. Do not invent or assume any parameters that are not mentioned.")] string parameter)
		{
			try
			{
				if (actionName.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return ToolExecutionResult.CreateErrorResult("Delete is not supported yet.");
				}
				bool bSuccess = Operation.dotStartAction(actionName, parameter);
				ToolExecutionResult toolExecutionResult = new ToolExecutionResult();
				toolExecutionResult.Success = bSuccess;
				toolExecutionResult.Message = "Executed with " + (bSuccess ? "success" : "failure") + " action(" + actionName + ")";
				return toolExecutionResult;
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("Failed to execute action (" + actionName + ") with parameter(s):" + parameter + "." + Environment.NewLine + "Error message:" + ex.Message, ex.Message);
			}
		}
	}
}
