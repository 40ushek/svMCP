using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to gather property report of selected objects.")]
	public class TeklaPropertyReportTool
	{
		private static readonly HashSet<string> DefaultIgnoredProperties = new HashSet<string> { "ModificationTime", "VisibilitySettings", "ID", "GUID", "StartPoint", "EndPoint" };

		[Description("Generates a property report for the currently selected Tekla Structures objects. Returns a list of properties that are inconsistent across the selection.")]
		public static ToolExecutionResult GeneratePropertyReport([Description("Property type to filter for. Possible values: UDA, Modifiable. If null, both are taken")] string propertyType, [Description("Selection identifier referencing previously stored IDs")] string cachedSelectionId, [Description("Whether to use the current selection in Tekla Structures")] string useCurrentSelectionString, [Description("Comma-separated list of explicit element IDs to query")] string elementIds, [Description("Comma-separated list of properties to ignore")] string propertiesToIgnore, [Description("Comma-separated list of specific properties to include (if empty, all properties are included)")] string propertiesToInclude, ISelectionCacheManager selectionCacheManager)
		{
			try
			{
				Model model = new Model();
				HashSet<string> ignoredProperties = new HashSet<string>();
				HashSet<string> includedProperties = null;
				if (!string.IsNullOrWhiteSpace(propertiesToInclude))
				{
					includedProperties = propertiesToInclude.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
				}
				if (!string.IsNullOrWhiteSpace(propertiesToIgnore))
				{
					ignoredProperties = propertiesToIgnore.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
				}
				foreach (string item in DefaultIgnoredProperties)
				{
					if (includedProperties == null || !includedProperties.Contains(item))
					{
						ignoredProperties.Add(item);
					}
				}
				SelectionResult selectionResult = ToolInputSelectionHandler.HandleInput(model, cachedSelectionId, useCurrentSelectionString, elementIds, null, int.MaxValue, 0, selectionCacheManager);
				if (!selectionResult.Success)
				{
					return ToolExecutionResult.CreateErrorResult(selectionResult.Message, null, selectionResult.Data);
				}
				if (selectionResult.Ids == null || selectionResult.Ids.Count == 0)
				{
					return ToolExecutionResult.CreateErrorResult("No elements found to analyze.");
				}
				int totalObjects = selectionResult.Ids.Count;
				Dictionary<string, HashSet<string>> propertyValueCounts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
				foreach (int id in selectionResult.Ids)
				{
					ModelObject modelObject = model.SelectModelObject(new Identifier(id));
					if (modelObject == null)
					{
						continue;
					}
					Dictionary<TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum, Dictionary<string, string>> serializedProps = SerializerFactory.CreateSerializer(modelObject).SerializeProperties(modelObject, 2, "", null, ignoredProperties, includedProperties);
					string text = propertyType?.ToUpperInvariant();
					string text2 = text;
					if (!(text2 == "UDA"))
					{
						if (text2 == "MODIFIABLE")
						{
							TrackPropertyValues(serializedProps[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.MODIFIABLE], propertyValueCounts);
							continue;
						}
						TrackPropertyValues(serializedProps[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.MODIFIABLE], propertyValueCounts);
						TrackPropertyValues(serializedProps[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.USER_DEFINED], propertyValueCounts);
					}
					else
					{
						TrackPropertyValues(serializedProps[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.USER_DEFINED], propertyValueCounts);
					}
				}
				List<string> inconsistentProperties = (from kvp in propertyValueCounts
					where kvp.Value.Count > 1
					select kvp.Key into name
					orderby name
					select name).ToList();
				return ToolExecutionResult.CreateSuccessResult($"Analyzed {propertyValueCounts.Count} properties over {totalObjects} objects. Found {inconsistentProperties.Count} properties with inconsistent values.", inconsistentProperties);
			}
			catch (Exception ex)
			{
				return ToolExecutionResult.CreateErrorResult("An error occurred while generating the property report.", ex.Message);
			}
		}

		private static void TrackPropertyValues(Dictionary<string, string> properties, Dictionary<string, HashSet<string>> propertyValueCounts)
		{
			foreach (KeyValuePair<string, string> property in properties)
			{
				if (!propertyValueCounts.TryGetValue(property.Key, out var values))
				{
					values = new HashSet<string>(StringComparer.Ordinal);
					propertyValueCounts[property.Key] = values;
				}
				values.Add(property.Value);
			}
		}
	}
}
