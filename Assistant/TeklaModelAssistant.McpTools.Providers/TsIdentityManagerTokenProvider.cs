using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Tekla.Structures.TeklaStructuresInternal;
using Trimble.Connect.Client;

namespace TeklaModelAssistant.McpTools.Providers
{
	public class TsIdentityManagerTokenProvider : ICredentialsProvider
	{
		private string AccessToken;

		public async Task AuthorizeAsync(HttpRequestMessage request)
		{
			if (request == null)
			{
				throw new ArgumentNullException("request");
			}
			if (AccessToken == null)
			{
				AtcUser? atcUser = await Operation.GetAtcUserAsync(false);
				if (!atcUser.HasValue)
				{
					throw new InvalidOperationException("AuthorizeAsync: getting atcUser failed");
				}
				AccessToken = atcUser.Value.AccessToken;
			}
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
		}

		public async Task InvalidateAndAuthorizeAsync(HttpRequestMessage request, HttpResponseMessage response)
		{
			if (request == null)
			{
				throw new ArgumentNullException("request");
			}
			AtcUser? atcUser = await Operation.GetAtcUserAsync(false);
			if (!atcUser.HasValue)
			{
				throw new InvalidOperationException("InvalidateAndAuthorizeAsync: getting atcUser failed");
			}
			AccessToken = atcUser.Value.AccessToken;
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
		}
	}
}
