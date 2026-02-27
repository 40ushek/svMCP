using System.Collections.Generic;

namespace TeklaMcpServer.Api.Model;

public interface IModelSelectionApi
{
    IReadOnlyList<ModelObjectInfo> GetSelectedObjects();

    int SelectObjectsByClass(int classNumber);

    SelectedWeightResult GetSelectedObjectsWeight();
}
