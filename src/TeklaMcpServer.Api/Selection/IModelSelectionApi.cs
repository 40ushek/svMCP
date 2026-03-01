using System.Collections.Generic;

namespace TeklaMcpServer.Api.Selection;

public interface IModelSelectionApi
{
    IReadOnlyList<ModelObjectInfo> GetSelectedObjects();

    int SelectObjectsByClass(int classNumber);

    SelectedWeightResult GetSelectedObjectsWeight();
}
