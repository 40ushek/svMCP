using System.Collections;
using System.Linq;
using System.Text.Json;

namespace TeklaModelAssistant.McpTools.Extensions
{
	public static class JsonExtensions
	{
		public static bool TryConvertFromJson<T>(this string json, out T result, JsonSerializerOptions options = null)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				result = default(T);
				return false;
			}
			try
			{
				result = JsonSerializer.Deserialize<T>(json, options);
				if (result == null)
				{
					return false;
				}
				if (typeof(IEnumerable).IsAssignableFrom(typeof(T)))
				{
					return result is IEnumerable enumerable && enumerable.Cast<object>().Any();
				}
				return true;
			}
			catch
			{
				result = default(T);
				return false;
			}
		}
	}
}
