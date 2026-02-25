using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TeklaModelAssistant.McpTools.Models
{
	public class TeklaContext
	{
		[JsonPropertyName("contextType")]
		public string ContextType { get; set; }

		[JsonPropertyName("hasConnection")]
		public bool HasConnection { get; set; }

		[JsonPropertyName("mode")]
		public string Mode { get; set; }

		[JsonPropertyName("modelName")]
		public string ModelName { get; set; }

		[JsonPropertyName("modelPath")]
		public string ModelPath { get; set; }

		[JsonPropertyName("drawingName")]
		public string DrawingName { get; set; }

		[JsonPropertyName("viewName")]
		public string ViewName { get; set; }

		[JsonPropertyName("availableViews")]
		public List<string> AvailableViews { get; set; }

		[JsonPropertyName("availableDrawings")]
		public List<string> AvailableDrawings { get; set; }

		[JsonPropertyName("environment")]
		public string Environment { get; set; }

		[JsonPropertyName("selectedObjectIds")]
		public List<int> SelectedObjectIds { get; set; }

		[JsonPropertyName("selectedCount")]
		public int SelectedCount { get; set; }

		public TeklaContext()
		{
			AvailableViews = new List<string>();
			AvailableDrawings = new List<string>();
			SelectedObjectIds = new List<int>();
		}
	}
}
