using System;
using Tekla.Structures.Geometry3d;

namespace TeklaModelAssistant.McpTools.Extensions
{
	public static class VectorExtensions
	{
		public static bool TryParseToVector(this string vectorString, out Vector result)
		{
			result = null;
			if (string.IsNullOrWhiteSpace(vectorString))
			{
				return false;
			}
			string[] parts = vectorString.Split(new char[2] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 3)
			{
				return false;
			}
			if (double.TryParse(parts[0], out var x) && double.TryParse(parts[1], out var y) && double.TryParse(parts[2], out var z))
			{
				result = new Vector(x, y, z);
				return true;
			}
			return false;
		}

		public static string ConvertToString(this Vector vector)
		{
			return $"{vector.X},{vector.Y},{vector.Z}";
		}
	}
}
