using System;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.ModelInternal;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to import reference models from the user's local disk.")]
	public class TeklaImportReferenceModelsTool
	{
		[Description("Imports the specified reference model into the current Tekla Structures model.")]
		public static ToolExecutionResult ImportReferenceModels([Description("A JSON array of absolute file paths of the reference models. Example: [\\\"C:\\\\some_file.ifc\\\" , \\\"C:\\\\other_file.dwg\\\"]")] string referenceModelPathsString)
		{
			if (!referenceModelPathsString.TryConvertFromJson<List<string>>(out var referenceModelPaths))
			{
				return ToolExecutionResult.CreateErrorResult("The 'referenceModelPathsString' argument is required and must be a valid JSON array of file paths.");
			}
			try
			{
				Model model = new Model();
				List<string> importedReferenceModels = new List<string>();
				List<string> failedReferenceModels = new List<string>();
				foreach (string referenceModelPath in referenceModelPaths)
				{
					ReferenceModel referenceModel = new ReferenceModel(referenceModelPath, new Point(0.0, 0.0, 0.0), 1.0);
					if (!referenceModel.Insert())
					{
						failedReferenceModels.Add(referenceModelPath);
					}
					else
					{
						importedReferenceModels.Add(referenceModelPath);
					}
				}
				if (importedReferenceModels.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No reference models were imported successfully.", null, new
					{
						FailedReferenceModels = failedReferenceModels
					});
				}
				bool fitWorkAreaResult = Operation.dotStartAction("FitWorkArea", "");
				model.CommitChanges("(TMA) ImportReferenceModels");
				return ToolExecutionResult.CreateSuccessResult($"{importedReferenceModels.Count} reference model(s) imported successfully.", new
				{
					ImportedReferenceModels = importedReferenceModels,
					FailedReferenceModels = failedReferenceModels,
					FitWorkAreaResult = fitWorkAreaResult
				});
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while importing reference models.", ex.Message);
			}
		}
	}
}
