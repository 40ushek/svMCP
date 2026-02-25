using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Internal;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Services;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tools to interact with the connected Trimble Connect project.")]
	public class TrimbleConnectTools
	{
		[Description("Lists all files from the linked Trimble Connect project. These files can then be downloaded and inserted in the model as reference models.")]
		public static async Task<ToolExecutionResult> ListTrimbleConnectProjectFiles(ITrimbleConnectService trimbleConnectService)
		{
			try
			{
				ConnectProjectInfo projectInfo = GetConnectProjectInfo();
				if (!(await trimbleConnectService.ConnectToProjectAsync(projectInfo)))
				{
					return ToolExecutionResult.CreateErrorResult("Failed to connect to the Trimble Connect project.");
				}
				return ToolExecutionResult.CreateSuccessResult("Files retrieved successfully.", await trimbleConnectService.ListProjectFilesAsync());
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				return ToolExecutionResult.CreateErrorResult("Failed to list files. Error: " + ex2.Message);
			}
		}

		[Description("Downloads a file from the linked Trimble Connect project to the local disk.The downloaded file then can be imported to the Tekla model as reference model.")]
		public static async Task<ToolExecutionResult> DownloadTrimbleConnectFiles([Description("JSON list of Trimble Connect file ids to download. Example: [\"bFU2a01Rn3Y\", \"bFU2a01Rn3T\"]")] string fileIdsString, ITrimbleConnectService trimbleConnectService)
		{
			if (!fileIdsString.TryConvertFromJson<List<string>>(out var fileIds))
			{
				return ToolExecutionResult.CreateErrorResult("The 'fileIdsString' argument must be a valid JSON list of strings representing Trimble Connect file IDs.");
			}
			try
			{
				ConnectProjectInfo projectInfo = GetConnectProjectInfo();
				bool isConnected = await trimbleConnectService.ConnectToProjectAsync(projectInfo);
				Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>();
				if (!isConnected)
				{
					return ToolExecutionResult.CreateErrorResult("Failed to connect to the Trimble Connect project.");
				}
				List<string> localFilePaths = new List<string>();
				foreach (string fileId in fileIds)
				{
					try
					{
						localFilePaths.Add(await trimbleConnectService.DownloadFileAsync(fileId));
					}
					catch
					{
						AddError(errors, fileId, "Failed to download file '" + fileId + "'. Skipping.");
					}
				}
				if (localFilePaths.Count > 0)
				{
					string message = $"Downloaded {localFilePaths.Count()} files out of {fileIds.Count()} files.";
					var toolResultData = new
					{
						downloadedFiles = localFilePaths,
						failedDownloads = errors
					};
					return ToolExecutionResult.CreateSuccessResult(message, toolResultData);
				}
				return ToolExecutionResult.CreateErrorResult("No files were downloaded.", null, errors);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("Failed to download files. Error: " + ex.Message);
			}
		}

		[Description("Uploads files to Trimble Connect")]
		public static async Task<ToolExecutionResult> UploadFilesToTrimbleConnect([Description("A serialized dictionary where the key is the path to the file and the value is a list of strings of the parent folders from Trimble Connect. Example: {\"C:\\\\Models\\\\Model_Name\\\\PlotFiles\\\\some_file.pdf\": [\"Drawings\"], \"C:\\\\Models\\\\Model2\\\\textFile.txt\": [\"Structures\", \"Info\"]}")] string filesDictionary, ITrimbleConnectService trimbleConnectService)
		{
			if (!filesDictionary.TryConvertFromJson<Dictionary<string, List<string>>>(out var filesToUpload))
			{
				return ToolExecutionResult.CreateErrorResult("The 'filesDictionary' argument must be a JSON dictionary mapping file paths to parent folder lists (e.g., { \"C:\\\\Models\\\\Model_Name\\\\PlotFiles\\\\some_file.pdf\": [\"Drawings\"] }), with at least one entry.");
			}
			try
			{
				ConnectProjectInfo projectInfo = GetConnectProjectInfo();
				bool isConnected = await trimbleConnectService.ConnectToProjectAsync(projectInfo);
				Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>();
				if (!isConnected)
				{
					return ToolExecutionResult.CreateErrorResult("Failed to connect to the Trimble Connect project.");
				}
				List<string> uploadResults = new List<string>();
				foreach (KeyValuePair<string, List<string>> fileUploadInfo in filesToUpload)
				{
					try
					{
						uploadResults.Add(await trimbleConnectService.UploadFileAsync(fileUploadInfo.Key, fileUploadInfo.Value));
					}
					catch
					{
						AddError(errors, fileUploadInfo.Key, "Failed to upload file '" + fileUploadInfo.Key + "'. Skipping.");
					}
				}
				if (uploadResults.Count > 0)
				{
					string message = $"Uploaded {uploadResults.Count()} files out of {filesToUpload.Count()} files.";
					var toolResultData = new
					{
						uploadedFiles = uploadResults,
						failedUploads = errors
					};
					return ToolExecutionResult.CreateSuccessResult(message, toolResultData);
				}
				return ToolExecutionResult.CreateErrorResult("No files were uploaded.");
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("Failed to upload files. Error: " + ex.Message);
			}
		}

		private static ConnectProjectInfo GetConnectProjectInfo()
		{
			Model model = new Model();
			if (!model.GetConnectionStatus())
			{
				throw new InvalidOperationException("No model is open.");
			}
			ProjectInfo projectInfo = model.GetProjectInfo();
			var (projectId, region) = Connect.GetProjectId(projectInfo);
			if (string.IsNullOrEmpty(projectId))
			{
				throw new InvalidOperationException("The current model is not linked to any Trimble Connect project.");
			}
			return new ConnectProjectInfo
			{
				ProjectId = projectId,
				Region = region
			};
		}

		private static void AddError(Dictionary<string, List<string>> errorLog, string id, string message)
		{
			if (!errorLog.ContainsKey(id))
			{
				errorLog[id] = new List<string>();
			}
			errorLog[id].Add(message);
		}
	}
}
