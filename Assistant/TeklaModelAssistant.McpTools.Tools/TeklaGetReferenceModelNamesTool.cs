using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to get reference model names.")]
	public class TeklaGetReferenceModelNamesTool
	{
		[Description("Gets the names and modification dates of all reference models in the current Tekla Structures model.The reference model names are ordered by newest first.Consider using this tool when the user doesn't specify a reference model or specifies a reference model by its creation time (i.e.: 'last imported reference model').")]
		public static ToolExecutionResult GetReferenceModelNames()
		{
			try
			{
				Model model = new Model();
				List<TeklaReferenceModelInfo> referenceModels = new List<TeklaReferenceModelInfo>();
				ModelObjectEnumerator enumerator = model.GetModelObjectSelector().GetAllObjectsWithType(ModelObject.ModelObjectEnum.REFERENCE_MODEL);
				while (enumerator.MoveNext())
				{
					if (enumerator.Current is ReferenceModel refModel)
					{
						referenceModels.Add(new TeklaReferenceModelInfo
						{
							Name = refModel.Title,
							ModificationTime = refModel.ModificationTime
						});
					}
				}
				return ToolExecutionResult.CreateSuccessResult($"Found {referenceModels.Count} reference models.", referenceModels.OrderByDescending((TeklaReferenceModelInfo rm) => rm.ModificationTime).ToList());
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An unexpected error occurred while retrieving reference model names.", ex.Message);
			}
		}
	}
}
