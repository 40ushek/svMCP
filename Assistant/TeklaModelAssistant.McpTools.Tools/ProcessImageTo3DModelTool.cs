using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Models.ImageProcessing;
using TeklaModelAssistant.McpTools.Services;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tool to convert 2D image drawings to 3D Tekla models via external Python service.")]
	public class ProcessImageTo3DModelTool
	{
		private const string DefaultModelName = "construction_drawings";

		private static readonly TimeSpan JobPollingInterval = TimeSpan.FromSeconds(3.0);

		private static readonly TimeSpan JobPollingTimeout = TimeSpan.FromMinutes(5.0);

		[Description("Processes a 2D drawing image (png, jpeg, jpg) by sending it to a Python service, which returns a .trb file that is then loaded into Tekla at the specified reference point.")]
		public static async Task<ToolExecutionResult> ProcessImageTo3DModel([Description("Full file path to the image file (png, jpeg, or jpg) containing the 2D drawing.")] string imageFilePath, [Description("Reference point where the model should be inserted, in format 'x,y,z'. Example: '0,0,0'. Defaults to origin if not specified.")] string referencePointString = "0,0,0", [Description("Scale factor for the imported model. Defaults to 1.0 if not specified.")] string scaleString = "1.0", IExt2D3DService ext2D3DService = null, IGraphToTrimbimService graphToTrimbimService = null)
		{
			if (ext2D3DService == null || graphToTrimbimService == null)
			{
				return ToolExecutionResult.CreateErrorResult("Image processing services are not configured. Please ensure IExt2D3DService and IGraphToTrimbimService are registered and provided.");
			}
			if (string.IsNullOrWhiteSpace(imageFilePath))
			{
				return ToolExecutionResult.CreateErrorResult("The 'imageFilePath' argument is required and cannot be empty.");
			}
			if (!File.Exists(imageFilePath))
			{
				return ToolExecutionResult.CreateErrorResult("The image file does not exist at path: " + imageFilePath);
			}
			string extension = Path.GetExtension(imageFilePath).ToLowerInvariant();
			if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
			{
				return ToolExecutionResult.CreateErrorResult("Invalid file type. Only .png, .jpg, and .jpeg files are supported. Got: " + extension);
			}
			if (!TryParsePoint(referencePointString, out var referencePoint))
			{
				return ToolExecutionResult.CreateErrorResult("The 'referencePointString' must be in format 'x,y,z'. Example: '0,0,0'");
			}
			if (!double.TryParse(scaleString, out var scale) || scale <= 0.0)
			{
				return ToolExecutionResult.CreateErrorResult("The 'scaleString' must be a positive number. Got: " + scaleString);
			}
			try
			{
				Model model = new Model();
				if (!model.GetConnectionStatus())
				{
					return ToolExecutionResult.CreateErrorResult("No Tekla model is currently open.");
				}
				string trbFilePath = await ProcessImage(imageFilePath, ext2D3DService, graphToTrimbimService);
				if (string.IsNullOrWhiteSpace(trbFilePath))
				{
					return ToolExecutionResult.CreateErrorResult("Python service did not return a valid .trb file path.");
				}
				if (!File.Exists(trbFilePath))
				{
					return ToolExecutionResult.CreateErrorResult("The returned .trb file does not exist at path: " + trbFilePath);
				}
				ToolExecutionResult loadResult = LoadTrbFileIntoTekla(trbFilePath, referencePoint, scale, model);
				if (!loadResult.Success)
				{
					return loadResult;
				}
				return ToolExecutionResult.CreateSuccessResult($"Successfully processed image and loaded 3D model into Tekla at point ({referencePoint.X}, {referencePoint.Y}, {referencePoint.Z})", new
				{
					ImageFile = imageFilePath,
					TrbFile = trbFilePath,
					ReferencePoint = referencePointString,
					Scale = scale
				});
			}
			catch (HttpRequestException ex)
			{
				HttpRequestException ex2 = ex;
				HttpRequestException httpEx = ex2;
				return ToolExecutionResult.CreateErrorResult("Failed to communicate with Python service.", "HTTP Error: " + ((Exception)(object)httpEx).Message);
			}
			catch (TaskCanceledException)
			{
				return ToolExecutionResult.CreateErrorResult("Request to Python service timed out. The processing may take too long.");
			}
			catch (Exception ex4)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while processing the image to 3D model.", ex4.Message);
			}
		}

		private static async Task<string> ProcessImage(string imageFilePath, IExt2D3DService ext2D3DService, IGraphToTrimbimService graphToTrimbimService)
		{
			UploadImageResponse uploadResponse = await ext2D3DService.UploadImageAsync(imageFilePath, "construction_drawings");
			if (uploadResponse == null || string.IsNullOrWhiteSpace(uploadResponse.SessionId) || string.IsNullOrWhiteSpace(uploadResponse.FileKey))
			{
				throw new HttpRequestException("Upload image response did not include the required session_id and file_key values.");
			}
			JobStatusResponse predictJobResponse = await ext2D3DService.CreateJobAsync(new JobRequest
			{
				JobType = "predict",
				SessionId = uploadResponse.SessionId,
				FileKey = uploadResponse.FileKey,
				ModelName = (string.IsNullOrWhiteSpace(uploadResponse.ModelName) ? "construction_drawings" : uploadResponse.ModelName)
			});
			if (predictJobResponse == null || string.IsNullOrWhiteSpace(predictJobResponse.JobId))
			{
				throw new HttpRequestException("Predict job creation failed because the job_id was missing in the response.");
			}
			JobStatusResponse completedPredictJob = await WaitForJobCompletionAsync(predictJobResponse.JobId, ext2D3DService);
			if (string.IsNullOrWhiteSpace(completedPredictJob.MaskKey))
			{
				throw new HttpRequestException("Predict job completed but did not return a mask_key.");
			}
			JobStatusResponse graphJobResponse = await ext2D3DService.CreateJobAsync(new JobRequest
			{
				JobType = "generate_graph",
				SessionId = uploadResponse.SessionId,
				FileKey = completedPredictJob.MaskKey
			});
			if (graphJobResponse == null || string.IsNullOrWhiteSpace(graphJobResponse.JobId))
			{
				throw new HttpRequestException("Generate_graph job creation failed because the job_id was missing in the response.");
			}
			await WaitForJobCompletionAsync(graphJobResponse.JobId, ext2D3DService);
			string graphJson = await ext2D3DService.DownloadGraphResultAsync(graphJobResponse.JobId);
			if (string.IsNullOrWhiteSpace(graphJson))
			{
				throw new HttpRequestException("Graph results endpoint returned an empty payload.");
			}
			return await graphToTrimbimService.ConvertGraphToTrimbimAsync(graphJson);
		}

		private static async Task<JobStatusResponse> WaitForJobCompletionAsync(string jobId, IExt2D3DService ext2D3DService)
		{
			DateTime start = DateTime.UtcNow;
			while (DateTime.UtcNow - start < JobPollingTimeout)
			{
				JobStatusResponse statusResponse = await ext2D3DService.GetJobStatusAsync(jobId);
				if (statusResponse == null)
				{
					throw new HttpRequestException("Failed to retrieve status for job " + jobId + ".");
				}
				if (string.Equals(statusResponse.Status, "completed", StringComparison.OrdinalIgnoreCase))
				{
					return statusResponse;
				}
				if (string.Equals(statusResponse.Status, "error", StringComparison.OrdinalIgnoreCase))
				{
					string errorMessage = (string.IsNullOrWhiteSpace(statusResponse.Error) ? "Unknown error returned by job endpoint." : statusResponse.Error);
					throw new HttpRequestException("Job " + jobId + " failed: " + errorMessage);
				}
				await System.Threading.Tasks.Task.Delay(JobPollingInterval);
			}
			throw new TimeoutException($"Timed out waiting for job {jobId} to complete after {JobPollingTimeout.TotalMinutes} minutes.");
		}

		private static ToolExecutionResult LoadTrbFileIntoTekla(string trbFilePath, Point referencePoint, double scale, Model model)
		{
			int handle = TrimbimPlacer.LoadAndPlaceTrimbim(trbFilePath, referencePoint, new Vector(1.0 * scale, 0.0, 0.0), new Vector(0.0, 1.0 * scale, 0.0));
			if (handle > 0)
			{
				model.CommitChanges("(TMA) ProcessImageTo3DModel");
				return ToolExecutionResult.CreateSuccessResult($"Successfully loaded .trb file into Tekla with handle {handle}.", new
				{
					TrimbimHandle = handle
				});
			}
			return ToolExecutionResult.CreateErrorResult("Failed to load the .trb file into Tekla. File: " + trbFilePath);
		}

		private static bool TryParsePoint(string pointString, out Point point)
		{
			point = null;
			if (string.IsNullOrWhiteSpace(pointString))
			{
				return false;
			}
			string[] parts = pointString.Split(',');
			if (parts.Length != 3)
			{
				return false;
			}
			if (double.TryParse(parts[0].Trim(), out var x) && double.TryParse(parts[1].Trim(), out var y) && double.TryParse(parts[2].Trim(), out var z))
			{
				point = new Point(x, y, z);
				return true;
			}
			return false;
		}
	}
}
