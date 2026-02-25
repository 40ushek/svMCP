using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Core
{
	public class ToolRegistry
	{
		private readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

		public void RegisterTool(ITool tool)
		{
			if (tool == null)
			{
				throw new ArgumentNullException("tool");
			}
			if (string.IsNullOrWhiteSpace(tool.Name))
			{
				throw new ArgumentException("Tool name cannot be empty", "tool");
			}
			_tools[tool.Name] = tool;
		}

		public ITool GetTool(string toolName)
		{
			if (string.IsNullOrWhiteSpace(toolName))
			{
				return null;
			}
			_tools.TryGetValue(toolName, out var tool);
			return tool;
		}

		public IEnumerable<ITool> GetAllTools()
		{
			return _tools.Values.ToList();
		}

		public async Task<ToolExecutionResult> ExecuteToolAsync(string toolName, string parametersJson)
		{
			try
			{
				ITool tool = GetTool(toolName);
				if (tool == null)
				{
					return ToolExecutionResult.CreateErrorResult("Tool '" + toolName + "' not found", "Tool '" + toolName + "' is not registered in the tool registry");
				}
				Dictionary<string, object> parameters = null;
				if (!string.IsNullOrWhiteSpace(parametersJson))
				{
					try
					{
						parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson);
					}
					catch (JsonException ex)
					{
						JsonException ex2 = ex;
						return ToolExecutionResult.CreateErrorResult("Invalid JSON parameters", ex2.Message);
					}
				}
				parameters = parameters ?? new Dictionary<string, object>();
				return await tool.ExecuteAsync(parameters);
			}
			catch (Exception ex3)
			{
				Exception ex4 = ex3;
				return ToolExecutionResult.CreateErrorResult("Error executing tool", ex4.Message);
			}
		}

		public List<ToolInfo> GetToolsInfo()
		{
			return _tools.Values.Select((ITool t) => new ToolInfo
			{
				Name = t.Name,
				Description = t.Description,
				Parameters = t.Parameters
			}).ToList();
		}
	}
}
