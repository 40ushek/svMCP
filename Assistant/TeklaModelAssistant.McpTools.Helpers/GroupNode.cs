using System.Collections.Generic;
using Tekla.Structures.Filtering;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class GroupNode : FilterNode
	{
		public List<FilterNode> Children { get; set; }

		public List<BinaryFilterOperatorType> Operators { get; set; }

		public GroupNode()
		{
			Children = new List<FilterNode>();
			Operators = new List<BinaryFilterOperatorType>();
		}

		public override string ToString()
		{
			return $"Group({Children.Count} children)";
		}
	}
}
