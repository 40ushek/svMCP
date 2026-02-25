namespace TeklaModelAssistant.McpTools.Tools
{
	public class AlignmentResult
	{
		public int? ModelId { get; set; }

		public string ModelName { get; set; }

		public bool Success { get; set; }

		public string Message { get; set; }

		public AlignmentResult(int? modelId, string modelName, bool success, string message)
		{
			ModelId = modelId;
			ModelName = modelName;
			Success = success;
			Message = message;
		}
	}
}
