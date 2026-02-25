using System.Collections.Generic;
using System.Threading.Tasks;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Models.Connect;

namespace TeklaModelAssistant.McpTools.Services
{
	public interface ITrimbleConnectService
	{
		Task<bool> ConnectToProjectAsync(ConnectProjectInfo connectProjectInfo);

		Task<List<ConnectFileSystemItem>> ListProjectFilesAsync();

		Task<string> DownloadFileAsync(string fileId);

		Task<string> UploadFileAsync(string localFilePath, List<string> parentCloudFolders);
	}
}
