using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Models.Connect;
using TeklaModelAssistant.McpTools.Providers;
using Trimble.Connect.Client;
using Trimble.Connect.Client.Models;

namespace TeklaModelAssistant.McpTools.Services
{
	public sealed class TrimbleConnectService : ITrimbleConnectService
	{
		private readonly TrimbleConnectClient _trimbleConnectClient;

		private readonly Uri _masterServiceUriProd = new Uri("https://app.connect.trimble.com/tc/api/2.0/");

		private readonly Uri _masterServiceUriStage = new Uri("https://app.stage.connect.trimble.com/tc/api/2.0/");

		private IProjectClient _projectClient;

		private List<ConnectFileSystemItem> _fileSystemItems;

		public bool IsConnected => _projectClient != null;

		public TrimbleConnectService(ICredentialsProvider credentialsProvider)
		{
			if (credentialsProvider == null)
			{
				throw new ArgumentNullException("credentialsProvider");
			}
			TrimbleConnectClientConfig config = new TrimbleConnectClientConfig
			{
				ServiceURI = (AtcEnvironmentProvider.IsAtcProductionEnvironment() ? _masterServiceUriProd : _masterServiceUriStage),
				RetryConfig = new RetryConfig
				{
					MaxErrorRetry = 1
				}
			};
			_trimbleConnectClient = new TrimbleConnectClient(config, credentialsProvider);
		}

		public async Task<bool> ConnectToProjectAsync(ConnectProjectInfo connectProjectInfo)
		{
			try
			{
				if (_projectClient != null && _projectClient.ProjectIdentifier.Equals(connectProjectInfo.ProjectId, StringComparison.InvariantCulture))
				{
					return true;
				}
				await _trimbleConnectClient.InitializeTrimbleConnectUserAsync();
				Uri serviceUri = (await _trimbleConnectClient.ReadConfigurationAsync()).FirstOrDefault((Region r) => r.Location == connectProjectInfo.Region)?.TcpsApi;
				_projectClient = _trimbleConnectClient.GetProjectClient(connectProjectInfo.ProjectId, serviceUri);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public async Task<List<ConnectFileSystemItem>> ListProjectFilesAsync()
		{
			if (!IsConnected)
			{
				throw new InvalidOperationException("ListProjectFilesAsync: Not connected to any project. Call ConnectToProjectAsync first.");
			}
			try
			{
				_fileSystemItems = new List<ConnectFileSystemItem>();
				Project project = await _projectClient.GetAsync();
				FolderItem rootFolderInfo = await _projectClient.Files.GetFolderInfoAsync(project.RootFolderIdentifier);
				await ProcessFolderContentsAsync(rootFolderInfo.Identifier, rootFolderInfo);
				return _fileSystemItems;
			}
			catch (Exception)
			{
				throw;
			}
		}

		public async Task<string> DownloadFileAsync(string fileId)
		{
			if (string.IsNullOrEmpty(fileId))
			{
				throw new ArgumentNullException("fileId", "DownloadFileAsync: File ID is null or empty.");
			}
			if (!IsConnected)
			{
				throw new InvalidOperationException("DownloadFileAsync: Not connected to any project. Call ConnectToProjectAsync first.");
			}
			try
			{
				FolderItem fileInfo = await _projectClient.Files.GetFileInfoAsync(fileId);
				if (fileInfo == null)
				{
					throw new FileNotFoundException("DownloadFileAsync: File with ID '" + fileId + "' not found in the connected project.");
				}
				string tempFilePath = Path.Combine(path2: fileInfo.Name, path1: Path.GetTempPath());
				using (Stream response = await _projectClient.Files.DownloadAsync(fileId))
				{
					using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
					{
						await response.CopyToAsync(fileStream);
						return tempFilePath;
					}
				}
			}
			catch (Exception)
			{
				throw;
			}
		}

		public async Task<string> UploadFileAsync(string localFilePath, List<string> parentCloudFolders)
		{
			if (string.IsNullOrEmpty(localFilePath))
			{
				throw new ArgumentNullException("localFilePath", "UploadFileAsync: Local file path is null or empty.");
			}
			if (!File.Exists(localFilePath))
			{
				throw new FileNotFoundException("UploadFileAsync: File '" + localFilePath + "' does not exist.");
			}
			if (!IsConnected)
			{
				throw new InvalidOperationException("UploadFileAsync: Not connected to any project. Call ConnectToProjectAsync first.");
			}
			try
			{
				Project project = await _projectClient.GetAsync();
				string fileName = Path.GetFileName(localFilePath);
				using (FileStream fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					string parentFolderId = project.RootFolderIdentifier;
					if (parentCloudFolders != null && parentCloudFolders.Count > 0)
					{
						foreach (string parentFolderName in parentCloudFolders)
						{
							FolderItem foundFolder = await FindFolder(parentFolderName, parentFolderId);
							parentFolderId = ((foundFolder == null) ? (await _projectClient.Files.CreateFolderAsync(parentFolderId, parentFolderName)).Identifier : foundFolder.Identifier);
						}
					}
					return (await _projectClient.Files.UploadAsync(parentFolderId, fileStream, fileName)).Identifier;
				}
			}
			catch
			{
				throw;
			}
		}

		private async Task<FolderItem> FindFolder(string folderName, string parentFolderId)
		{
			foreach (FolderItem item in await _projectClient.Files.GetFolderItemsAsync(parentFolderId))
			{
				if (item.Name.Equals(folderName, StringComparison.InvariantCultureIgnoreCase) && item.IsFolder)
				{
					return item;
				}
			}
			return null;
		}

		private async Task ProcessFolderContentsAsync(string folderId, FolderItem parentItem)
		{
			try
			{
				foreach (FolderItem folderItem in await _projectClient.Files.GetFolderItemsAsync(folderId))
				{
					if (folderItem.IsFolder)
					{
						await ProcessFolderContentsAsync(folderItem.Identifier, folderItem);
						continue;
					}
					_fileSystemItems.Add(new ConnectFileSystemItem
					{
						Id = folderItem.Identifier,
						Name = folderItem.Name
					});
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
