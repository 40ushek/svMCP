namespace TeklaMcpServer.Api.Filtering
{
	public class SimpleExpressionNode : FilterNode
	{
		public string Category { get; set; } = string.Empty;

		public string Property { get; set; } = string.Empty;

		public string Operator { get; set; } = string.Empty;

		public string Value { get; set; } = string.Empty;

		public override string ToString()
		{
			return Category + "|" + Property + "|" + Operator + "|" + Value;
		}
	}
}

