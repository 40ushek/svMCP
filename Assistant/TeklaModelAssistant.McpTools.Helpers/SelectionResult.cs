using System.Collections.Generic;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class SelectionResult
	{
		public bool Success { get; set; }

		public IList<int> Ids { get; set; }

		public string EffectiveSelectionId { get; set; }

		public int Total { get; set; }

		public bool HasMore { get; set; }

		public string Message { get; set; }

		public string Data { get; set; }
	}
}
