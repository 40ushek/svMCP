using System;
using System.Globalization;
using Tekla.Structures.Geometry3d;

namespace TeklaModelAssistant.McpTools.Extensions
{
	public static class PointExtensions
	{
		public static bool TryParseToPoint(this string pointString, out Point result)
		{
			result = null;
			if (string.IsNullOrWhiteSpace(pointString))
			{
				return false;
			}
			string cleanString = pointString.Trim().Replace("(", "").Replace(")", "");
			string[] parts = cleanString.Split(new char[2] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 3)
			{
				return false;
			}
			if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
			{
				result = new Point(x, y, z);
				return true;
			}
			return false;
		}

		public static string ConvertToString(this Point point)
		{
			return $"{point.X},{point.Y},{point.Z}";
		}
	}
}
