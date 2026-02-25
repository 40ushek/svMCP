using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to export drawings to pdf")]
	public class TeklaDrawingsExportToPdfTool
	{
		[Description("Export drawings to pdf")]
		public static async Task<ToolExecutionResult> ExportDrawingsToPdf([Description("List of drawing guids in a JSON array format. Example: \"[\\\"8a59aef7-6b5a-4181-8f81-25b4d8ea5e62\\\",\\\"b54b92bf-4f96-438a-b962-9acad4f2df7b\\\".")] string drawingsIdentifiersListString)
		{
			try
			{
				Model model = new Model();
				if (!model.GetConnectionStatus())
				{
					return ToolExecutionResult.CreateErrorResult("No model is open.");
				}
				DrawingHandler drawingHandler = new DrawingHandler();
				List<Drawing> drawingsToExport = new List<Drawing>();
				List<Drawing> outdatedDrawings = new List<Drawing>();
				drawingHandler.SaveActiveDrawing();
				if (!drawingsIdentifiersListString.TryConvertFromJson<List<string>>(out var drawingsIds))
				{
					return ToolExecutionResult.CreateErrorResult("The 'drawingsIdentifiersListString' argument must be a JSON array of strings with at least one entry.");
				}
				DrawingEnumerator drawingEnumerator = drawingHandler.GetDrawings();
				while (drawingEnumerator.MoveNext())
				{
					Drawing drawing = drawingEnumerator.Current;
					if (drawingsIds.Contains(drawing.GetIdentifier().GUID.ToString()))
					{
						if (drawing.IsUpToDate())
						{
							drawingsToExport.Add(drawing);
							drawingHandler.SetActiveDrawing(drawing);
							drawingHandler.SaveActiveDrawing();
							drawingHandler.CloseActiveDrawing();
						}
						else
						{
							outdatedDrawings.Add(drawing);
						}
					}
				}
				if (drawingsToExport.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No drawings available for export", null, new
					{
						OutdatedDrawings = outdatedDrawings
					});
				}
				string modelPath = model.GetInfo().ModelPath;
				List<string> exportedDrawingsPath = await ExportToPdf(drawingsToExport, drawingHandler, modelPath);
				if (exportedDrawingsPath.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("Failed to export any drawing to PDF.", null, new
					{
						ExportedDrawings = exportedDrawingsPath,
						OutdatedDrawings = outdatedDrawings
					});
				}
				return ToolExecutionResult.CreateSuccessResult($"Exported '{exportedDrawingsPath.Count}' drawings out of {drawingsToExport.Count}", new
				{
					ExportedDrawings = exportedDrawingsPath,
					OutdatedDrawings = outdatedDrawings
				});
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while exporting drawings to PDF.", ex2.Message);
			}
		}

		private static async Task<List<string>> ExportToPdf(List<Drawing> drawingsToExport, DrawingHandler drawingHandler, string modelPath)
		{
			TaskCompletionSource<List<string>> tsc = new TaskCompletionSource<List<string>>();
			List<string> exportFilesPaths = new List<string>();
			Thread thread = new Thread((ThreadStart)delegate
			{
				DPMPrinterAttributes printAttributes = new DPMPrinterAttributes
				{
					PrinterName = "Microsoft Print to PDF",
					OutputType = DotPrintOutputType.PDF
				};
				string path = Path.Combine(modelPath, "PlotFiles");
				if (!Directory.Exists(path))
				{
					Directory.CreateDirectory(path);
				}
				foreach (Drawing current in drawingsToExport)
				{
					try
					{
						string path2 = current.Name + "_" + current.Mark + ".pdf";
						string text = Path.Combine(modelPath, "PlotFiles", path2);
						if (drawingHandler.PrintDrawing(current, printAttributes, text))
						{
							exportFilesPaths.Add(text);
						}
					}
					catch
					{
					}
				}
				tsc.SetResult(exportFilesPaths);
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			return await tsc.Task;
		}
	}
}
