using System.Collections.Generic;
using System.Threading.Tasks;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Core
{
	public interface ITool
	{
		string Name { get; }

		string Description { get; }

		List<ToolParameter> Parameters { get; }

		Task<ToolExecutionResult> ExecuteAsync(Dictionary<string, object> parameters);
	}
}
