namespace TeklaModelAssistant.McpTools.Extensions
{
	public static class StringExtensions
	{
		public static bool TryConvertFromJson(this string value, out bool result)
		{
			result = false;
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}
			value = value.Trim().ToLowerInvariant();
			if (value == "true" || value == "1")
			{
				result = true;
				return true;
			}
			if (value == "false" || value == "0")
			{
				result = false;
				return true;
			}
			return bool.TryParse(value, out result);
		}
	}
}
