using System.Threading.Tasks;
using TeklaModelAssistant.McpTools.Models.ImageProcessing;

namespace TeklaModelAssistant.McpTools.Services
{
	public interface IExt2D3DService
	{
		Task<UploadImageResponse> UploadImageAsync(string imageFilePath, string modelName);

		Task<JobStatusResponse> CreateJobAsync(JobRequest jobRequest);

		Task<JobStatusResponse> GetJobStatusAsync(string jobId);

		Task<string> DownloadGraphResultAsync(string graphJobId);
	}
}
