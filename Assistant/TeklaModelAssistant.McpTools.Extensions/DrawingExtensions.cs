using System.Collections.Generic;
using Tekla.Structures.Drawing;

namespace TeklaModelAssistant.McpTools.Extensions
{
	public static class DrawingExtensions
	{
		private static readonly HashSet<DrawingUpToDateStatus> upToDateStatuses = new HashSet<DrawingUpToDateStatus>
		{
			DrawingUpToDateStatus.DrawingIsUpToDate,
			DrawingUpToDateStatus.DrawingIsUpToDateButMayNeedChecking,
			DrawingUpToDateStatus.DrawingWasCloned,
			DrawingUpToDateStatus.DrawingWasUpdated,
			DrawingUpToDateStatus.DrawingWasSplitted,
			DrawingUpToDateStatus.DrawingWasClonedFromCloud
		};

		public static bool IsUpToDate(this Drawing drawing)
		{
			return upToDateStatuses.Contains(drawing.UpToDateStatus);
		}
	}
}
