namespace TeklaModelAssistant.McpTools.Helpers
{
	public class Token
	{
		public TokenType Type { get; set; }

		public string Value { get; set; }

		public int Position { get; set; }

		public override string ToString()
		{
			return $"{Type}('{Value}') at position {Position}";
		}
	}
}
