using System.Collections.Generic;

namespace TeklaModelAssistant.McpTools.Models
{
	public class ConnectionCreationInput
	{
		public string ConnectionName { get; set; }

		public int ConnectionNumber { get; set; }

		public int PrimaryPartIdentifier { get; set; }

		public List<int> SecondaryPartIdentifiers { get; set; } = new List<int>();

		public string AttributesFile { get; set; }

		public string FileExtension { get; set; }

		public Dictionary<string, string> PropertySet { get; set; } = new Dictionary<string, string>();
	}
}
