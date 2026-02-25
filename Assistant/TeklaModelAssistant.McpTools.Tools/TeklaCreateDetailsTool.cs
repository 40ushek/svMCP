using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Models.Creation;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to create details. ")]
	public class TeklaCreateDetailsTool
	{
		private const int MaxDetailsPerCall = 50;

		[Description("Create details based on the provided parameters. A detail is different from a connection in that it only connects to one part.")]
		public static ToolExecutionResult CreateDetails([Description("Property sets of the details to be created. It is a list of dictionaries in JSON format (max 50 entries).Required parameters: DetailName, DetailNumber, PrimaryPartIdentifier, ReferencePoint, FileExtension. Reference point is a comma separated string of the coordinates x,y,z.Optional: AttributesFile, PropertySet that its a dictionary of additional properties to set on the detail. Key is the property name, value is its value.CRITICAL: Do NOT include optional properties (Name, Profile, Material, Class, etc.) in the JSON unless the user explicitly requests them as overrides.Example: [ { \"DetailName\": \"U.S. Seat Detail 2\", \"DetailNumber\": 1049, \"PrimaryPartIdentifier\": 12345, \"ReferencePoint\": \"0,0,0\", \"FileExtension\": \".j1049\" } ]")] string detailCreationInputListString)
		{
			if (!detailCreationInputListString.TryConvertFromJson<List<DetailCreationInput>>(out var detailCreationInputList))
			{
				return ToolExecutionResult.CreateErrorResult("Failed to parse 'detailCreationInputListString' argument. Ensure it is a valid JSON list of DetailCreationInput objects.");
			}
			if (detailCreationInputList.Count > 50)
			{
				return ToolExecutionResult.CreateErrorResult($"Too many model objects in one call. Maximum is {50}, received {detailCreationInputList.Count}. Split into multiple calls.");
			}
			Model model = new Model();
			List<object> createdDetails = new List<object>();
			List<object> failedDetails = new List<object>();
			foreach (DetailCreationInput detailInput in detailCreationInputList)
			{
				if (!TryCreateDetail(model, detailInput, out var detail, out var errorMessage))
				{
					failedDetails.Add(new
					{
						DetailNumber = detailInput.DetailNumber,
						Error = errorMessage
					});
					continue;
				}
				switch (detail.Status)
				{
				case ConnectionStatusEnum.STATUS_OK:
					createdDetails.Add(new
					{
						DetailId = detail.Identifier.ToString(),
						Warning = errorMessage,
						Status = "OK"
					});
					break;
				case ConnectionStatusEnum.STATUS_WARNING:
					createdDetails.Add(new
					{
						DetailId = detail.Identifier.ToString(),
						Warning = errorMessage,
						Status = "WARNING"
					});
					break;
				case ConnectionStatusEnum.STATUS_ERROR:
					createdDetails.Add(new
					{
						DetailId = detail.Identifier.ToString(),
						Warning = errorMessage,
						Status = "ERROR"
					});
					break;
				default:
					createdDetails.Add(new
					{
						DetailId = detail.Identifier.ToString(),
						Warning = errorMessage,
						Status = "UNKNOWN"
					});
					break;
				}
			}
			if (createdDetails.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult($"No details were created. {failedDetails.Count} failures.", null, new
				{
					FailedDetails = failedDetails
				});
			}
			model.CommitChanges("(TMA) CreateDetails");
			return ToolExecutionResult.CreateSuccessResult($"Created {createdDetails.Count} out of {detailCreationInputList.Count} details.", new
			{
				CreatedDetails = createdDetails,
				FailedDetails = failedDetails
			});
		}

		private static bool TryCreateDetail(Model model, DetailCreationInput detailCreationInput, out Detail detail, out string errorMessage)
		{
			detail = null;
			try
			{
				detail = new Detail
				{
					Name = detailCreationInput.DetailName,
					Number = detailCreationInput.DetailNumber
				};
				ModelObject primaryPart = model.SelectModelObject(new Identifier(detailCreationInput.PrimaryPartIdentifier));
				if (primaryPart == null)
				{
					errorMessage = $"Primary part with identifier {detailCreationInput.PrimaryPartIdentifier} not found.";
					return false;
				}
				detail.SetPrimaryObject(primaryPart);
				if (!detailCreationInput.ReferencePoint.TryParseToPoint(out var referencePoint))
				{
					errorMessage = "Reference point is not valid. It should be a comma separated string of the coordinates x,y,z.";
					return false;
				}
				detail.SetReferencePoint(referencePoint);
				if (!string.IsNullOrEmpty(detailCreationInput.AttributesFile))
				{
					detail.LoadAttributesFromFile(detailCreationInput.AttributesFile);
				}
				StringBuilder messageBuilder = new StringBuilder();
				foreach (KeyValuePair<string, string> property in detailCreationInput.PropertySet)
				{
					try
					{
						if (!PropertyAccessHelper.TrySetPropertyValue(detail, property.Key, property.Value))
						{
							messageBuilder.AppendLine("Property " + property.Key + " could not be set.");
						}
					}
					catch (Exception ex)
					{
						messageBuilder.AppendLine("Error setting property " + property.Key + ": " + ex.Message);
					}
				}
				if (!detail.Insert())
				{
					errorMessage = "Failed to insert detail " + detail.Name + ".";
					return false;
				}
				errorMessage = messageBuilder.ToString();
				return true;
			}
			catch (Exception ex2)
			{
				errorMessage = "Exception occurred while creating detail " + detailCreationInput.DetailName + ": " + ex2.Message;
				return false;
			}
		}
	}
}
