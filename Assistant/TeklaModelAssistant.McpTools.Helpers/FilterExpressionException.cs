using System;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class FilterExpressionException : Exception
	{
		public string Category { get; }

		public string Property { get; }

		public string Operator { get; }

		public string Value { get; }

		public FilterExpressionException(string message, string category = null, string property = null, string @operator = null, string value = null)
			: base(message)
		{
			Category = category;
			Property = property;
			Operator = @operator;
			Value = value;
		}

		public FilterExpressionException(string message, Exception innerException, string category = null, string property = null, string @operator = null, string value = null)
			: base(message, innerException)
		{
			Category = category;
			Property = property;
			Operator = @operator;
			Value = value;
		}
	}
}
