using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Tekla.Structures.TeklaStructuresInternal;
using TeklaModelAssistant.McpTools.Models.ImageProcessing;

namespace TeklaModelAssistant.McpTools.Services
{
	public sealed class Ext2D3DService : IExt2D3DService
	{
		private const string ApiBaseUrl = "https://ext-2d3d.trimbleai.com/api";

		private const string UploadImageEndpoint = "https://ext-2d3d.trimbleai.com/api/upload_image";

		private const string JobsEndpoint = "https://ext-2d3d.trimbleai.com/api/jobs/";

		private const string GraphResultsEndpoint = "https://ext-2d3d.trimbleai.com/api/results/graphs";

		private static readonly HttpClient HttpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5.0)
		};

		private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		public async Task<UploadImageResponse> UploadImageAsync(string imageFilePath, string modelName)
		{
			string uploadUrl = "https://ext-2d3d.trimbleai.com/api/upload_image?model_name=" + Uri.EscapeDataString(modelName ?? string.Empty);
			string bearerToken = await GetBearerTokenAsync();
			MultipartFormDataContent content = new MultipartFormDataContent();
			try
			{
				using (FileStream imageStream = File.OpenRead(imageFilePath))
				{
					StreamContent imageContent = new StreamContent((Stream)imageStream);
					try
					{
						((HttpContent)imageContent).Headers.ContentType = new MediaTypeHeaderValue(GetContentType(imageFilePath));
						content.Add((HttpContent)(object)imageContent, "image", Path.GetFileName(imageFilePath));
						HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
						try
						{
							request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
							request.Content = (HttpContent)(object)content;
							HttpResponseMessage response = await HttpClient.SendAsync(request);
							string responseContent = await response.Content.ReadAsStringAsync();
							if (!response.IsSuccessStatusCode)
							{
								throw new HttpRequestException($"Upload image endpoint returned {response.StatusCode}: {responseContent}");
							}
							UploadImageResponse uploadResponse = JsonSerializer.Deserialize<UploadImageResponse>(responseContent, JsonOptions);
							if (uploadResponse == null)
							{
								throw new HttpRequestException("Failed to parse upload image response.");
							}
							return uploadResponse;
						}
						finally
						{
							((IDisposable)request)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)imageContent)?.Dispose();
					}
				}
			}
			finally
			{
				((IDisposable)content)?.Dispose();
			}
		}

		public async Task<JobStatusResponse> CreateJobAsync(JobRequest jobRequest)
		{
			string bearerToken = await GetBearerTokenAsync();
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://ext-2d3d.trimbleai.com/api/jobs/");
			try
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				string payload = JsonSerializer.Serialize(jobRequest, JsonOptions);
				request.Content = (HttpContent)new StringContent(payload, Encoding.UTF8, "application/json");
				HttpResponseMessage response = await HttpClient.SendAsync(request);
				string responseContent = await response.Content.ReadAsStringAsync();
				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Jobs endpoint returned {response.StatusCode} when creating '{jobRequest?.JobType}' job: {responseContent}");
				}
				JobStatusResponse jobResponse = JsonSerializer.Deserialize<JobStatusResponse>(responseContent, JsonOptions);
				if (jobResponse == null)
				{
					throw new HttpRequestException("Failed to parse job creation response.");
				}
				return jobResponse;
			}
			finally
			{
				((IDisposable)request)?.Dispose();
			}
		}

		public async Task<JobStatusResponse> GetJobStatusAsync(string jobId)
		{
			string bearerToken = await GetBearerTokenAsync();
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://ext-2d3d.trimbleai.com/api/jobs/" + jobId);
			try
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
				HttpResponseMessage response = await HttpClient.SendAsync(request);
				string responseContent = await response.Content.ReadAsStringAsync();
				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Jobs endpoint returned {response.StatusCode} when polling job {jobId}: {responseContent}");
				}
				JobStatusResponse statusResponse = JsonSerializer.Deserialize<JobStatusResponse>(responseContent, JsonOptions);
				if (statusResponse == null)
				{
					throw new HttpRequestException("Failed to parse job status response.");
				}
				return statusResponse;
			}
			finally
			{
				((IDisposable)request)?.Dispose();
			}
		}

		public async Task<string> DownloadGraphResultAsync(string graphJobId)
		{
			string bearerToken = await GetBearerTokenAsync();
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://ext-2d3d.trimbleai.com/api/results/graphs/" + graphJobId);
			try
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
				HttpResponseMessage response = await HttpClient.SendAsync(request);
				string responseContent = await response.Content.ReadAsStringAsync();
				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Graph results endpoint returned {response.StatusCode} for job {graphJobId}: {responseContent}");
				}
				return responseContent;
			}
			finally
			{
				((IDisposable)request)?.Dispose();
			}
		}

		private static async Task<string> GetBearerTokenAsync()
		{
			try
			{
				string token = (await Operation.GetAtcUserAsync(false)).Value.AccessToken;
				if (string.IsNullOrWhiteSpace(token))
				{
					throw new InvalidOperationException("Tekla Structures returned an empty bearer token.");
				}
				return token;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				throw new InvalidOperationException("Failed to obtain Tekla bearer token. Please ensure the application is authorized.", ex2);
			}
		}

		private static string GetContentType(string filePath)
		{
			string extension = Path.GetExtension(filePath).ToLowerInvariant();
			if (1 == 0)
			{
			}
			string result;
			switch (extension)
			{
			case ".png":
				result = "image/png";
				break;
			case ".jpg":
				result = "image/jpeg";
				break;
			case ".jpeg":
				result = "image/jpeg";
				break;
			default:
				result = "application/octet-stream";
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}
}
