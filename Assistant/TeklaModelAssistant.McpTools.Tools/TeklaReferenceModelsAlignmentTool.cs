using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.ModelInternal;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to align reference models")]
	public class TeklaReferenceModelsAlignmentTool
	{
		[Description("Tool used for aligning one or more reference models to either a basepoint or another reference model.Requires active view.")]
		public static ToolExecutionResult AlignReferenceModels([Description("Names of reference models to align in JSON array format. Example: [\"Model1\"] or [\"Model1\", \"Model2\", \"Model3\"]")] string modelNames, [Description("Type of alignment: 'ReferenceModel' or 'BasePoint'")] string alignmentType, [Description("Target to align to: For 'ReferenceModel' type - name of reference model.For 'BasePoint' type - name of base point (empty/null uses project base point)")] string alignTarget = null)
		{
			ToolExecutionResult validationResult = ValidateParameters(modelNames, alignmentType);
			if (validationResult != null)
			{
				return validationResult;
			}
			try
			{
				Model model = new Model();
				List<string> parsedModelNames = ParseModelNames(modelNames);
				if (parsedModelNames.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No valid model names provided. Use format: [\"Model1\", \"Model2\"] (valid JSON array)");
				}
				List<AlignmentResult> results = new List<AlignmentResult>();
				foreach (string currentModelName in parsedModelNames)
				{
					results.Add(ProcessSingleModel(model, currentModelName, alignmentType, alignTarget));
				}
				int successCount = results.Count((AlignmentResult r) => r.Success);
				if (successCount > 0)
				{
					bool fitWorkAreaResult = Operation.dotStartAction("FitWorkArea", "");
					model.CommitChanges("(TMA) AlignReferenceModels");
				}
				return BuildResponse(parsedModelNames, results, alignmentType, alignTarget);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while aligning reference models.", ex.Message);
			}
		}

		private static ToolExecutionResult ValidateParameters(string modelNames, string alignmentType)
		{
			if (string.IsNullOrWhiteSpace(modelNames))
			{
				return ToolExecutionResult.CreateErrorResult("The 'modelNames' argument is required and cannot be empty.");
			}
			if (string.IsNullOrWhiteSpace(alignmentType))
			{
				return ToolExecutionResult.CreateErrorResult("The 'alignmentType' argument is required and must be either 'ReferenceModel' or 'BasePoint'.");
			}
			return null;
		}

		private static List<string> ParseModelNames(string modelNames)
		{
			string trimmedModelNames = modelNames?.Trim();
			if (string.IsNullOrWhiteSpace(trimmedModelNames))
			{
				return new List<string>();
			}
			try
			{
				List<string> parsedNames = JsonSerializer.Deserialize<List<string>>(trimmedModelNames);
				if (parsedNames != null)
				{
					return (from name in parsedNames
						where !string.IsNullOrWhiteSpace(name)
						select name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				}
			}
			catch (JsonException)
			{
			}
			return new List<string>();
		}

		private static AlignmentResult ProcessSingleModel(Model model, string referenceModelName, string alignmentType, string alignTarget)
		{
			ReferenceModel targetModel = FindReferenceModelByName(model, referenceModelName);
			if (targetModel == null)
			{
				return new AlignmentResult(null, referenceModelName, false, "Reference model '" + referenceModelName + "' not found. Use GetReferenceModelNames to list available models.");
			}
			string alignmentTypeUpper = alignmentType.ToUpper();
			string text = alignmentTypeUpper;
			string text2 = text;
			bool success;
			string message;
			if (text2 == "REFERENCEMODEL")
			{
				(success, message) = AlignToReferenceModel(model, targetModel, referenceModelName, alignTarget);
			}
			else if (text2 == "BASEPOINT")
			{
				(success, message) = AlignToBasePoint(targetModel, referenceModelName, alignTarget);
			}
			else
			{
				success = false;
				message = "Invalid alignment type: '" + alignmentType + "'. Must be 'ReferenceModel' or 'BasePoint'";
			}
			return new AlignmentResult(targetModel.Identifier?.ID, referenceModelName, success, message);
		}

		private static (bool success, string message) AlignToReferenceModel(Model model, ReferenceModel targetModel, string modelName, string alignTarget)
		{
			if (string.IsNullOrWhiteSpace(alignTarget))
			{
				return (success: false, message: "Reference model name is required for ReferenceModel alignment");
			}
			ReferenceModel alignToModel = FindReferenceModelByName(model, alignTarget);
			if (alignToModel == null)
			{
				return (success: false, message: "Reference model '" + alignTarget + "' to align to not found. Use GetReferenceModelNames to list available models.");
			}
			CopyReferenceModelProperties(targetModel, alignToModel);
			bool success = targetModel.Modify();
			string message = (success ? ("Successfully aligned '" + modelName + "' to reference model '" + alignTarget + "'") : ("Failed to align '" + modelName + "' to reference model '" + alignTarget + "'"));
			return (success: success, message: message);
		}

		private static (bool success, string message) AlignToBasePoint(ReferenceModel targetModel, string modelName, string alignTarget)
		{
			var (basePoint, errorMessage) = GetBasePoint(alignTarget);
			if (basePoint == null)
			{
				return (success: false, message: errorMessage);
			}
			targetModel.BasePointGuid = basePoint.Guid;
			targetModel.Position = new Point(basePoint.LocationInModelX, basePoint.LocationInModelY, basePoint.LocationInModelZ);
			bool success = targetModel.Modify();
			string basePointDescription = (string.IsNullOrWhiteSpace(alignTarget) ? "project base point" : ("base point '" + alignTarget + "'"));
			string message = (success ? ("Successfully aligned '" + modelName + "' to " + basePointDescription) : ("Failed to align '" + modelName + "' to " + basePointDescription));
			return (success: success, message: message);
		}

		private static void CopyReferenceModelProperties(ReferenceModel target, ReferenceModel source)
		{
			if (source.BasePointGuid != Guid.Empty)
			{
				target.BasePointGuid = source.BasePointGuid;
			}
			target.Position = new Point(source.Position.X, source.Position.Y, source.Position.Z);
			target.Scale = source.Scale;
			if (source.Rotation3D != null)
			{
				target.Rotation3D = source.Rotation3D;
			}
		}

		private static (BasePoint basePoint, string errorMessage) GetBasePoint(string basePointName)
		{
			if (string.IsNullOrWhiteSpace(basePointName))
			{
				BasePoint projectBasePoint = ProjectInfo.GetProjectBasePoint();
				return ((BasePoint basePoint, string errorMessage))((projectBasePoint != null && projectBasePoint.Guid != Guid.Empty) ? (basePoint: projectBasePoint, errorMessage: null) : (basePoint: null, errorMessage: "No project base point is set on the current model"));
			}
			BasePoint namedBasePoint = ProjectInfo.GetBasePointByName(basePointName);
			return ((BasePoint basePoint, string errorMessage))((namedBasePoint != null && namedBasePoint.Guid != Guid.Empty) ? (basePoint: namedBasePoint, errorMessage: null) : (basePoint: null, errorMessage: "Base point '" + basePointName + "' not found"));
		}

		private static ToolExecutionResult BuildResponse(List<string> modelNames, List<AlignmentResult> results, string alignmentType, string alignTarget)
		{
			int successCount = results.Count((AlignmentResult r) => r.Success);
			int failedCount = results.Count - successCount;
			string message = $"Processed {modelNames.Count} model(s): {successCount} succeeded, {failedCount} failed";
			string alignedTo = (alignmentType.Equals("REFERENCEMODEL", StringComparison.OrdinalIgnoreCase) ? alignTarget : (alignTarget ?? "Project Base Point"));
			return new ToolExecutionResult
			{
				Success = (successCount > 0),
				Message = message,
				Data = new
				{
					summary = new
					{
						totalModels = modelNames.Count,
						successCount = successCount,
						failedCount = failedCount
					},
					alignmentType = alignmentType,
					alignedTo = alignedTo,
					detailedResults = results.Select((AlignmentResult r) => new
					{
						modelId = r.ModelId,
						modelName = r.ModelName,
						success = r.Success,
						message = r.Message
					})
				},
				Error = ((successCount == 0) ? message : null)
			};
		}

		private static ReferenceModel FindReferenceModelByName(Model model, string name)
		{
			ModelObjectEnumerator modelObjectEnumerator = model.GetModelObjectSelector().GetAllObjectsWithType(ModelObject.ModelObjectEnum.REFERENCE_MODEL);
			while (modelObjectEnumerator.MoveNext())
			{
				if (modelObjectEnumerator.Current is ReferenceModel refModel && !string.IsNullOrEmpty(refModel.Filename))
				{
					string fileNameWithoutExt = Path.GetFileNameWithoutExtension(refModel.Filename);
					string fileName = Path.GetFileName(refModel.Filename);
					if (fileNameWithoutExt.Equals(name, StringComparison.OrdinalIgnoreCase) || fileName.Equals(name, StringComparison.OrdinalIgnoreCase) || refModel.Filename.Equals(name, StringComparison.OrdinalIgnoreCase))
					{
						return refModel;
					}
				}
			}
			return null;
		}
	}
}
