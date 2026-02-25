namespace TeklaModelAssistant.McpTools.Models
{
	public class ComponentCreationInput
	{
		public int PartId { get; set; }

		public string ComponentName { get; set; }

		public int ComponentNumber { get; set; }

		public string Point { get; set; }

		public string Point2 { get; set; }

		public string AdditionalPartIds { get; set; }
	}
}
