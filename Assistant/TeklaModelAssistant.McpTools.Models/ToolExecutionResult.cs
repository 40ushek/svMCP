using System.Text.Json.Serialization;

namespace TeklaModelAssistant.McpTools.Models
{
	public class ToolExecutionResult
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("message")]
		public string Message { get; set; }

		[JsonPropertyName("data")]
		public object Data { get; set; }

		[JsonPropertyName("error")]
		public string Error { get; set; }

		public static ToolExecutionResult CreateErrorResult(string message, string error = null, object data = null)
		{
			return new ToolExecutionResult
			{
				Success = false,
				Message = message,
				Data = data,
				Error = error
			};
		}

		public static ToolExecutionResult CreateSuccessResult(string message, object data = null)
		{
			return new ToolExecutionResult
			{
				Success = true,
				Message = message,
				Data = data
			};
		}
	}
}
