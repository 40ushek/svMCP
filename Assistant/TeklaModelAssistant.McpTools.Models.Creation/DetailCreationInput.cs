using System.Collections.Generic;

namespace TeklaModelAssistant.McpTools.Models.Creation
{
	public class DetailCreationInput
	{
		public string DetailName { get; set; }

		public int DetailNumber { get; set; }

		public int PrimaryPartIdentifier { get; set; }

		public string ReferencePoint { get; set; }

		public string AttributesFile { get; set; }

		public string FileExtension { get; set; }

		public Dictionary<string, string> PropertySet { get; set; } = new Dictionary<string, string>();
	}
}
