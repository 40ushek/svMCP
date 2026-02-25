using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TeklaModelAssistant.McpTools.Services
{
	public sealed class GraphToTrimbimService : IGraphToTrimbimService
	{
		private const string GraphToTrimbimEndpoint = "https://graphtotrimbim-bhfaa7h6e5ewd6gk.eastus-01.azurewebsites.net/api/graphToTrimbim";

		private static readonly HttpClient HttpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5.0)
		};

		public async Task<string> ConvertGraphToTrimbimAsync(string graphJson)
		{
			string graphToTrimbimUrl = Environment.GetEnvironmentVariable("GRAPH_TO_TRIMBIM_URL") ?? GraphToTrimbimEndpoint;
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, graphToTrimbimUrl);
			try
			{
				request.Content = (HttpContent)new StringContent(graphJson, Encoding.UTF8, "application/json");
				HttpResponseMessage response = await HttpClient.SendAsync(request);
				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException(string.Format(arg1: await response.Content.ReadAsStringAsync(), format: "GraphToTrimbim service returned {0}: {1}", arg0: response.StatusCode));
				}
				string tempDirectory = Path.Combine(Path.GetTempPath(), "TeklaModelAssistant_2DTo3D");
				Directory.CreateDirectory(tempDirectory);
				string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string trbFileName = $"model_{timestamp}_{Guid.NewGuid():N}.trb";
				string trbFilePath = Path.Combine(tempDirectory, trbFileName);
				using (FileStream fileStream = new FileStream(trbFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					await response.Content.CopyToAsync((Stream)fileStream);
				}
				return trbFilePath;
			}
			finally
			{
				((IDisposable)request)?.Dispose();
			}
		}
	}
}
