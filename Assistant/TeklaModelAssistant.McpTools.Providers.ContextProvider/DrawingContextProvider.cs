using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider
{
	public static class DrawingContextProvider
	{
		public static readonly HashSet<string> IgnoreProperties = new HashSet<string> { "ModificationTime", "VisibilitySettings" };

		public static string CollectContext([System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			DrawingHandler drawingHandler = new DrawingHandler();
			if (!drawingHandler.GetConnectionStatus())
			{
				throw new Exception("Failed to extract drawing context");
			}
			DrawingObjectEnumerator selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
			List<Dictionary<string, Dictionary<string, string>>> value = CollectPropertiesFromSelectedDrawingObjects(selected);
			Dictionary<string, string> generalDrawingInfo = GetGeneralDrawingInfo();
			Dictionary<string, object> commonContext = CommonContextProvider.CollectContext();
			Dictionary<string, object> value2 = new Dictionary<string, object>
			{
				{ "generalDrawingInfo", generalDrawingInfo },
				{ "selectedObjects", value },
				{
					"additionalInfo",
					(selected.GetSize() > 0) ? "These are all the currently selected objects in drawing, access these by:\nDrawingHandler drawingHandler = new DrawingHandler();\nTekla.Structures.Drawing.UI.DrawingObjectSelector selector = drawingHandler.GetDrawingObjectSelector();\nDrawingObjectEnumerator selectedObjects = selector.GetSelected();\nwhile(selectedObjects.MoveNext()) { // Do Stuff }" : ""
				},
				{ "commonContext", commonContext }
			};
			return JsonConvert.SerializeObject(value2);
		}

		private static Dictionary<string, Dictionary<string, string>> DrawingSerializeWrapper(object obj)
		{
			Dictionary<string, Dictionary<string, string>> dictionary = new Dictionary<string, Dictionary<string, string>>();
			ISerializer serializer = SerializerFactory.CreateSerializer(obj);
			Dictionary<TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum, Dictionary<string, string>> dictionary2 = serializer.SerializeProperties(obj, 2, "", null, IgnoreProperties);
			Dictionary<string, string> value = dictionary2[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.READ_ONLY];
			Dictionary<string, string> value2 = dictionary2[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.MODIFIABLE];
			Dictionary<string, string> value3 = dictionary2[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.TEMPLATE];
			Dictionary<string, string> value4 = dictionary2[TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer.PropertyTypeEnum.USER_DEFINED];
			string drawingObjectId = ((obj is DatabaseObject databaseObject) ? databaseObject.GetIdentifier().ID.ToString() : "");
			string modelObjectId = "";
			Dictionary<string, string> basicModelInfo = null;
			if (obj is Tekla.Structures.Drawing.ModelObject drawingModelObject && drawingModelObject.ModelIdentifier != null)
			{
				modelObjectId = drawingModelObject.ModelIdentifier.ID.ToString();
				basicModelInfo = GetBasicModelProperties(drawingModelObject.ModelIdentifier);
			}
			dictionary["objectInfo"] = new Dictionary<string, string>
			{
				{
					"Tekla Open API Class Name",
					obj.GetType().Name
				},
				{ "Drawing.Identifier.ID", drawingObjectId },
				{ "Model.Identifier.ID", modelObjectId }
			};
			if (basicModelInfo != null && basicModelInfo.Count > 0)
			{
				dictionary["basicModelInfo"] = basicModelInfo;
			}
			dictionary["readOnly"] = value;
			dictionary["modifiable"] = value2;
			dictionary["template"] = value3;
			dictionary["userDefined"] = value4;
			return dictionary;
		}

		private static Dictionary<string, string> GetBasicModelProperties(Identifier modelIdentifier)
		{
			Dictionary<string, string> basicInfo = new Dictionary<string, string>();
			try
			{
				Model model = new Model();
				if (!model.GetConnectionStatus())
				{
					return basicInfo;
				}
				Tekla.Structures.Model.ModelObject modelObject = model.SelectModelObject(modelIdentifier);
				if (modelObject == null)
				{
					return basicInfo;
				}
				(string, string)[] basicProperties = new(string, string)[4]
				{
					("Name", "NAME"),
					("Profile", "PROFILE.PROFILESTRING"),
					("Material", "MATERIAL.MATERIALSTRING"),
					("Class", "CLASS")
				};
				(string, string)[] array = basicProperties;
				for (int i = 0; i < array.Length; i++)
				{
					var (displayName, propertyPath) = array[i];
					if (PropertyAccessHelper.TryGetPropertyValue(modelObject, propertyPath, out var propValue))
					{
						string valueStr = propValue?.ToString()?.Trim();
						if (!string.IsNullOrEmpty(valueStr))
						{
							basicInfo[displayName] = valueStr;
						}
					}
				}
			}
			catch
			{
			}
			return basicInfo;
		}

		private static Dictionary<string, string> GetGeneralDrawingInfo()
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			DrawingHandler drawingHandler = new DrawingHandler();
			if (!drawingHandler.GetConnectionStatus())
			{
				return dictionary;
			}
			Drawing activeDrawing = drawingHandler.GetActiveDrawing();
			if (activeDrawing == null)
			{
				return dictionary;
			}
			ContainerView sheet = activeDrawing.GetSheet();
			if (sheet == null)
			{
				return dictionary;
			}
			StringBuilder stringBuilder = new StringBuilder();
			dictionary["activeDrawing.Name"] = activeDrawing.Name;
			dictionary["activeDrawing.Sheet.Width"] = sheet.Width.ToString();
			dictionary["activeDrawing.Sheet.Height"] = sheet.Height.ToString();
			RectangleBoundingBox axisAlignedBoundingBox = sheet.GetAxisAlignedBoundingBox();
			dictionary["activeDrawing.Sheet.BoundingBox.MinPoint"] = $"({axisAlignedBoundingBox.MinPoint.X}, {axisAlignedBoundingBox.MinPoint.Y})";
			dictionary["activeDrawing.Sheet.BoundingBox.MaxPoint"] = $"({axisAlignedBoundingBox.MaxPoint.X}, {axisAlignedBoundingBox.MaxPoint.Y})";
			DrawingObjectEnumerator views = sheet.GetViews();
			int num = 1;
			while (views.MoveNext())
			{
				if (views.Current is ViewBase viewBase)
				{
					dictionary[$"activeDrawing.Sheet.View{num}.Width"] = viewBase.Width.ToString();
					dictionary[$"activeDrawing.Sheet.View{num}.Height"] = viewBase.Height.ToString();
					RectangleBoundingBox axisAlignedBoundingBox2 = viewBase.GetAxisAlignedBoundingBox();
					dictionary[$"activeDrawing.Sheet.View{num}.BoundingBox.MinPoint"] = $"({axisAlignedBoundingBox2.MinPoint.X}, {axisAlignedBoundingBox2.MinPoint.Y})";
					dictionary[$"activeDrawing.Sheet.View{num}.BoundingBox.MaxPoint"] = $"({axisAlignedBoundingBox2.MaxPoint.X}, {axisAlignedBoundingBox2.MaxPoint.Y})";
					num++;
				}
			}
			return dictionary;
		}

		private static List<Dictionary<string, Dictionary<string, string>>> CollectPropertiesFromSelectedDrawingObjects(DrawingObjectEnumerator selectedObjects)
		{
			List<Dictionary<string, Dictionary<string, string>>> list = new List<Dictionary<string, Dictionary<string, string>>>();
			while (selectedObjects.MoveNext())
			{
				DrawingObject current = selectedObjects.Current;
				if (current != null)
				{
					Dictionary<string, Dictionary<string, string>> item = DrawingSerializeWrapper(current);
					list.Add(item);
				}
			}
			return list;
		}
	}
}
