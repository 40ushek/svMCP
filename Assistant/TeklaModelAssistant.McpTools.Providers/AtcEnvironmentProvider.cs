using Fusion;

namespace TeklaModelAssistant.McpTools.Providers
{
	public class AtcEnvironmentProvider
	{
		private static readonly string AtcUrlRegistryValueName = "AccountTeklaCom.EndpointUrl";

		private static readonly string AtcUrlProd = "https://account.tekla.com";

		public static bool IsAtcProductionEnvironment()
		{
			try
			{
				string atcEndPointInUse = ((IKeyValueStore)App.Settings)[AtcUrlRegistryValueName];
				return string.IsNullOrEmpty(atcEndPointInUse) || NormalizeUrl(atcEndPointInUse) == NormalizeUrl(AtcUrlProd);
			}
			catch
			{
				return false;
			}
			string NormalizeUrl(string url)
			{
				return url?.Trim().TrimEnd('/').ToLowerInvariant();
			}
		}
	}
}
