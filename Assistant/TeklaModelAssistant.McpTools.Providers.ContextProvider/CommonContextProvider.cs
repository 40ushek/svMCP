using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider
{
	public static class CommonContextProvider
	{
		public static Dictionary<string, object> CollectContext()
		{
			DrawingHandler drawingHandler = new DrawingHandler();
			if (!drawingHandler.GetConnectionStatus())
			{
				return new Dictionary<string, object>();
			}
			DrawingEnumerator selectedDrawings = drawingHandler.GetDrawingSelector().GetSelected();
			List<string> selectedDrawingsIds = new List<string>();
			while (selectedDrawings.MoveNext())
			{
				Drawing currentDrawing = selectedDrawings.Current;
				if (currentDrawing != null)
				{
					selectedDrawingsIds.Add(currentDrawing.GetIdentifier().GUID.ToString());
				}
			}
			return new Dictionary<string, object> { { "selectedDrawingsIds", selectedDrawingsIds } };
		}
	}
}
