using System.Collections.Generic;

namespace TeklaModelAssistant.McpTools.Core
{
	public class ToolInfo
	{
		public string Name { get; set; }

		public string Description { get; set; }

		public List<ToolParameter> Parameters { get; set; }
	}
}
