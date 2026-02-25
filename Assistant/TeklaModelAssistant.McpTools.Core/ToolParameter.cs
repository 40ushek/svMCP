namespace TeklaModelAssistant.McpTools.Core
{
	public class ToolParameter
	{
		public string Name { get; set; }

		public string Description { get; set; }

		public string Type { get; set; }

		public bool IsRequired { get; set; }

		public object DefaultValue { get; set; }
	}
}
