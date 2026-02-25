using System.Collections.Generic;

namespace TeklaModelAssistant.McpTools.Managers
{
	public interface ISelectionCacheManager
	{
		string CreateSelection(IEnumerable<int> ids);

		bool TryGetIdsBySelectionId(string selectionId, out List<int> ids);
	}
}
