using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tools for inserting components (macros)")]
	public class TeklaCreateComponentsTool
	{
		private const int MaxComponentsPerCall = 20;

		[Description("Create one or many Tekla Components. Only call when user explicitly wants to CREATE/INSERT. ONLY use components from knowledge base (TeklaKnowledge-CreateComponents), if it's not listed there, it doesn't exist. When a component requires Point coordinates, offer the user options: 1) relative to part (beam start/end, slab center), 2) use TeklaPointPickerTool to pick interactively, 3) manual coordinates (x,y,z).")]
		public static ToolExecutionResult CreateComponents([Description("JSON array (max 20 entries). Example: [{\"PartId\":123,\"ComponentName\":\"Beam reinforcement\",\"ComponentNumber\":30000063,\"Point\":\"0,0,0\",\"Point2\":\"100,0,0\",\"AdditionalPartIds\":\"456,789\"}]")] string componentsList)
		{
			if (!ValidateComponentsList(componentsList, out var components, out var error))
			{
				return error;
			}
			Model model = new Model();
			List<object> createdComponents = new List<object>();
			List<object> failedComponents = new List<object>();
			foreach (ComponentCreationInput component in components)
			{
				if (TryCreateComponent(model, component, out var componentId, out var errorMessage))
				{
					createdComponents.Add(new { component.PartId, componentId });
				}
				else
				{
					failedComponents.Add(new
					{
						PartId = component.PartId,
						error = errorMessage
					});
				}
			}
			if (createdComponents.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult("No components were created.", null, new { failedComponents });
			}
			model.CommitChanges("(TMA) CreateComponents");
			return ToolExecutionResult.CreateSuccessResult($"Created {createdComponents.Count}/{components.Count} component(s).", new
			{
				successCount = createdComponents.Count,
				totalCount = components.Count,
				createdComponents = createdComponents,
				failedComponents = failedComponents
			});
		}

		private static bool ValidateComponentsList(string componentsList, out List<ComponentCreationInput> components, out ToolExecutionResult error)
		{
			components = null;
			error = null;
			if (string.IsNullOrWhiteSpace(componentsList))
			{
				error = ToolExecutionResult.CreateErrorResult("componentsList is required.");
				return false;
			}
			if (!componentsList.TryConvertFromJson<List<ComponentCreationInput>>(out components))
			{
				error = ToolExecutionResult.CreateErrorResult("Invalid JSON format. Expected: [{PartId, ComponentName, ComponentNumber, ...}, ...]");
				return false;
			}
			if (components.Count > 20)
			{
				error = ToolExecutionResult.CreateErrorResult($"Too many components in one call. Maximum is {20}, received {components.Count}. Split into multiple calls.");
				return false;
			}
			return true;
		}

		private static bool TryCreateComponent(Model model, ComponentCreationInput input, out string componentId, out string errorMessage)
		{
			componentId = null;
			errorMessage = null;
			try
			{
				if (!TryGetPart(model, input.PartId, out var part, out errorMessage))
				{
					return false;
				}
				if (!TryParsePoints(input.Point, input.Point2, out var parsedPoint, out var parsedPoint2, out errorMessage))
				{
					return false;
				}
				if (!TryGetAdditionalParts(model, input.AdditionalPartIds, out var additionalParts, out errorMessage))
				{
					return false;
				}
				ComponentInput componentInput = BuildComponentInput(part, additionalParts, parsedPoint, parsedPoint2);
				Tekla.Structures.Model.Component component = new Tekla.Structures.Model.Component
				{
					Name = input.ComponentName,
					Number = input.ComponentNumber
				};
				component.SetComponentInput(componentInput);
				if (!component.Insert())
				{
					errorMessage = $"Tekla rejected component insertion for part {input.PartId}.";
					return false;
				}
				componentId = component.Identifier.ToString();
				return true;
			}
			catch (Exception ex)
			{
				errorMessage = $"Failed for part {input.PartId}: {ex.Message}";
				return false;
			}
		}

		private static bool TryGetPart(Model model, int partId, out Part part, out string errorMessage)
		{
			part = null;
			errorMessage = null;
			ModelObject modelObject = model.SelectModelObject(new Identifier(partId));
			if (modelObject is Part p)
			{
				part = p;
				return true;
			}
			errorMessage = $"Part with id {partId} was not found.";
			return false;
		}

		private static bool TryParsePoints(string point, string point2, out Point parsedPoint, out Point parsedPoint2, out string errorMessage)
		{
			parsedPoint = null;
			parsedPoint2 = null;
			errorMessage = null;
			if (!string.IsNullOrWhiteSpace(point) && !point.TryParseToPoint(out parsedPoint))
			{
				errorMessage = "First point format is invalid. Use \"x,y,z\".";
				return false;
			}
			if (!string.IsNullOrWhiteSpace(point2) && !point2.TryParseToPoint(out parsedPoint2))
			{
				errorMessage = "Second point format is invalid. Use \"x,y,z\".";
				return false;
			}
			return true;
		}

		private static bool TryGetAdditionalParts(Model model, string partIds, out List<Part> parts, out string errorMessage)
		{
			parts = null;
			errorMessage = null;
			if (string.IsNullOrWhiteSpace(partIds))
			{
				return true;
			}
			parts = new List<Part>();
			List<string> invalidIds = new List<string>();
			string[] ids = partIds.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string[] array = ids;
			foreach (string idStr in array)
			{
				string trimmedId = idStr.Trim();
				if (!int.TryParse(trimmedId, out var id))
				{
					invalidIds.Add(trimmedId);
					continue;
				}
				if (!TryGetPart(model, id, out var part, out errorMessage))
				{
					return false;
				}
				parts.Add(part);
			}
			if (parts.Count == 0 && ids.Length != 0)
			{
				errorMessage = "No valid part IDs found in AdditionalPartIds. Invalid values: " + string.Join(", ", invalidIds);
				return false;
			}
			return true;
		}

		private static ComponentInput BuildComponentInput(Part primaryPart, List<Part> additionalParts, Point point, Point point2)
		{
			ComponentInput input = new ComponentInput();
			input.AddInputObject(primaryPart);
			if (additionalParts != null && additionalParts.Count > 0)
			{
				if (additionalParts.Count == 1)
				{
					input.AddInputObject(additionalParts[0]);
				}
				else
				{
					ArrayList arrayList = new ArrayList(additionalParts);
					input.AddInputObjects(arrayList);
				}
			}
			if (point != null && point2 != null)
			{
				input.AddTwoInputPositions(point, point2);
			}
			else if (point != null)
			{
				input.AddOneInputPosition(point);
			}
			return input;
		}
	}
}
