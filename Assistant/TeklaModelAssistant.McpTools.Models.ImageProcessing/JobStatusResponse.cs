using System.Text.Json.Serialization;

namespace TeklaModelAssistant.McpTools.Models.ImageProcessing
{
	public class JobStatusResponse
	{
		[JsonPropertyName("job_id")]
		public string JobId { get; set; }

		[JsonPropertyName("status")]
		public string Status { get; set; }

		[JsonPropertyName("session_id")]
		public string SessionId { get; set; }

		[JsonPropertyName("error")]
		public string Error { get; set; }

		[JsonPropertyName("mask_key")]
		public string MaskKey { get; set; }

		[JsonPropertyName("graph_key")]
		public string GraphKey { get; set; }

		[JsonPropertyName("converted_image_key")]
		public string ConvertedImageKey { get; set; }
	}
}
