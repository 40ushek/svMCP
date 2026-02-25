namespace TeklaModelAssistant.McpTools.Helpers
{
	public class SimpleExpressionNode : FilterNode
	{
		public string Category { get; set; }

		public string Property { get; set; }

		public string Operator { get; set; }

		public string Value { get; set; }

		public override string ToString()
		{
			return Category + "|" + Property + "|" + Operator + "|" + Value;
		}
	}
}
