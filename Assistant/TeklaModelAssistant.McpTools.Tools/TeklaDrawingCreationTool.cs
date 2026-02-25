using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tekla.Structures;
using Tekla.Structures.Model.Operations;
using Tekla.Structures.Model.UI;
using Tekla.Structures.ModelInternal;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for creating and managing Tekla drawings.")]
	public class TeklaDrawingCreationTool
	{
		[Description("Creates a new General Arrangement (GA) drawing using the currently active model view.")]
		public static async Task<ToolExecutionResult> CreateGeneralArrangementDrawing([Description("The name of the saved drawing properties file to apply (defaults to 'standard').")] string drawingProperties, [Description("Specify whether to open the drawing after creation. Accepts 'true' or 'false'. Defaults to 'true'.")] string openDrawingString)
		{
			if (string.IsNullOrWhiteSpace(drawingProperties))
			{
				drawingProperties = "standard";
			}
			if (!bool.TryParse(openDrawingString, out var openDrawingValue))
			{
				openDrawingValue = true;
			}
			View modelView = Tekla.Structures.ModelInternal.Operation.GetCurrentView();
			if (modelView == null || string.IsNullOrWhiteSpace(modelView.Name))
			{
				return ToolExecutionResult.CreateErrorResult("Current view is invalid or unsaved. Please save the view first.");
			}
			if (!(await CreateGaDrawingViaMacroAsync(modelView.Name, drawingProperties, openDrawingValue)))
			{
				return ToolExecutionResult.CreateErrorResult("Failed to create GA drawing.");
			}
			return ToolExecutionResult.CreateSuccessResult("GA drawing created successfully.");
		}

		public static async Task<bool> CreateGaDrawingViaMacroAsync(string viewName, string gaAttribute, bool openGaDrawing)
		{
			if (string.IsNullOrWhiteSpace(viewName))
			{
				throw new ArgumentException("View name must be supplied.", "viewName");
			}
			string macroDirs = string.Empty;
			if (!TeklaStructuresSettings.GetAdvancedOption("XS_MACRO_DIRECTORY", ref macroDirs))
			{
				throw new InvalidOperationException("XS_MACRO_DIRECTORY is not defined.");
			}
			string modelingDir = null;
			string[] paths = macroDirs.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			string[] array = paths;
			foreach (string path in array)
			{
				string cleanPath = path.Trim();
				string subDir = Path.Combine(cleanPath, "modeling");
				if (Directory.Exists(subDir))
				{
					modelingDir = subDir;
					break;
				}
				if (Directory.Exists(cleanPath))
				{
					modelingDir = cleanPath;
					break;
				}
			}
			if (modelingDir == null)
			{
				throw new DirectoryNotFoundException("Valid modeling macro directory not found.");
			}
			string macroName = $"_tmp_ga_{Guid.NewGuid():N}.cs";
			string macroPath = Path.Combine(modelingDir, macroName);
			viewName = viewName.Replace("\\", "\\\\").Replace("\"", "\\\"");
			string attrLine = (string.IsNullOrWhiteSpace(gaAttribute) ? string.Empty : ("            akit.ValueChange(\"Create GA-drawing\", \"dia_attr_name\", \"" + gaAttribute + "\");" + Environment.NewLine));
			string openFlag = (openGaDrawing ? "1" : "0");
			string macroSource = "\r\n            namespace Tekla.Technology.Akit.UserScript\r\n            {\r\n                public sealed class Script\r\n                {\r\n                    public static void Run(Tekla.Technology.Akit.IScript akit)\r\n                    {\r\n                        akit.Callback(\"acmd_create_dim_general_assembly_drawing\", \"\", \"main_frame\");\r\n            " + attrLine + "            akit.ListSelect(\"Create GA-drawing\", \"dia_view_name_list\", \"" + viewName + "\");\r\n                        akit.ValueChange(\"Create GA-drawing\", \"dia_creation_mode\", \"0\");\r\n                        akit.ValueChange(\"Create GA-drawing\", \"dia_open_drawing\", \"" + openFlag + "\");\r\n                        akit.PushButton(\"Pushbutton_127\", \"Create GA-drawing\");\r\n                    }\r\n                }\r\n            }";
			File.WriteAllText(macroPath, macroSource);
			try
			{
				return await Task.Run(delegate
				{
					if (!Tekla.Structures.Model.Operations.Operation.RunMacro(macroName))
					{
						return false;
					}
					DateTime dateTime = DateTime.Now.AddSeconds(30.0);
					while (Tekla.Structures.Model.Operations.Operation.IsMacroRunning())
					{
						if (DateTime.Now > dateTime)
						{
							return false;
						}
						Thread.Sleep(100);
					}
					return true;
				});
			}
			finally
			{
				if (File.Exists(macroPath))
				{
					File.Delete(macroPath);
				}
			}
		}
	}
}
