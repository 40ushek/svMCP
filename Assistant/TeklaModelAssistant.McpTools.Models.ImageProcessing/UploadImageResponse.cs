using System.Text.Json.Serialization;

namespace TeklaModelAssistant.McpTools.Models.ImageProcessing
{
	public class UploadImageResponse
	{
		[JsonPropertyName("session_id")]
		public string SessionId { get; set; }

		[JsonPropertyName("file_key")]
		public string FileKey { get; set; }

		[JsonPropertyName("model_name")]
		public string ModelName { get; set; }
	}
}
