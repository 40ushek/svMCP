using System;
using System.Collections.Generic;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public static class DimensionDrawingTextBoxDebugReader
{
    public static List<DrawingTextBox> Collect(View view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        using var presentationConnection = DimensionTextBoxContextLoader.TryCreatePresentationConnection();
        if (presentationConnection == null)
            return [];

        var sources = DimensionTextBoxContextLoader.CollectDimensionTextBoxSources(view);
        return DimensionDrawingTextBoxCollector.CollectDistinct(
            presentationConnection,
            sources,
            view);
    }
}
