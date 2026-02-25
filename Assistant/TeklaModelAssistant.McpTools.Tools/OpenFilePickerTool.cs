using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Generic tool for opening a file picker.")]
	public class OpenFilePickerTool
	{
		[Description("Opens a native file selection dialog for the user to select one or more files. IMPORTANT: Execute this tool only if the user explicitly agrees to it. Ask the user clearly if he wants to pick a file, do not assume their response!")]
		public static async Task<ToolExecutionResult> OpenFilePicker([Description("A filter string to specify allowable file types. Example: 'IFC Files (*.ifc)|*.ifc|All Files (*.*)|*.*'")] string fileFilter)
		{
			if (string.IsNullOrWhiteSpace(fileFilter))
			{
				return ToolExecutionResult.CreateErrorResult("The 'fileFilter' argument is required and cannot be empty.");
			}
			try
			{
				List<string> selectedFilePaths = await SelectFileOnStaThreadAsync(fileFilter);
				if (selectedFilePaths != null && selectedFilePaths.Count > 0)
				{
					return ToolExecutionResult.CreateSuccessResult($"User selected {selectedFilePaths.Count} file(s).", selectedFilePaths);
				}
				return ToolExecutionResult.CreateErrorResult("File selection was cancelled by the user.");
			}
			catch (OperationCanceledException)
			{
				return ToolExecutionResult.CreateErrorResult("Operation was cancelled.");
			}
			catch (Exception ex2)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while opening the file picker.", ex2.Message);
			}
		}

		private static async Task<List<string>> SelectFileOnStaThreadAsync(string fileFilter)
		{
			TaskCompletionSource<List<string>> tcs = new TaskCompletionSource<List<string>>();
			Thread uiThread = new Thread((ThreadStart)delegate
			{
				try
				{
					using (OpenFileDialog openFileDialog = new OpenFileDialog())
					{
						openFileDialog.Filter = fileFilter;
						openFileDialog.Title = "Select File(s)";
						openFileDialog.Multiselect = true;
						if (openFileDialog.ShowDialog() == DialogResult.OK)
						{
							tcs.TrySetResult(openFileDialog.FileNames.ToList());
						}
						else
						{
							tcs.TrySetResult(new List<string>());
						}
					}
				}
				catch (Exception exception)
				{
					tcs.TrySetException(exception);
				}
			});
			uiThread.SetApartmentState(ApartmentState.STA);
			uiThread.Start();
			return await tcs.Task;
		}
	}
}
