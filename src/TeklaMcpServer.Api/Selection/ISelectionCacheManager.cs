using System.Collections.Generic;

namespace TeklaMcpServer.Api.Selection;

public interface ISelectionCacheManager
{
    string? CreateSelection(IEnumerable<int> ids);

    bool TryGetIdsBySelectionId(string? selectionId, out List<int> ids);
}
