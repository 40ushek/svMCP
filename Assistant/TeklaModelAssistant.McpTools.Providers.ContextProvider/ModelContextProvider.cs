using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider
{
	public static class ModelContextProvider
	{
		public static readonly HashSet<string> IgnoreProperties = new HashSet<string> { "ModificationTime", "VisibilitySettings" };

		public static string CollectContext([System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			ViewCamera viewCamera = new ViewCamera();
			viewCamera.View = ViewHandler.GetActiveView();
			if (viewCamera.View == null)
			{
				throw new Exception("Failed to extract context. No view is open.");
			}
			viewCamera.Select();
			Tekla.Structures.Model.UI.ModelObjectSelector modelObjectSelector = new Tekla.Structures.Model.UI.ModelObjectSelector();
			ModelObjectEnumerator selectedObjects = modelObjectSelector.GetSelectedObjects();
			List<Dictionary<string, Dictionary<string, string>>> list = new List<Dictionary<string, Dictionary<string, string>>>();
			List<Dictionary<string, Dictionary<string, string>>> list2 = new List<Dictionary<string, Dictionary<string, string>>>();
			list2.AddRange(CollectPropertiesFromSelectedObjects(selectedObjects));
			list.Add(ModelSerializeWrapper(viewCamera));
			list.Add(ModelSerializeWrapper(ViewHandler.GetActiveView()));
			Dictionary<string, object> commonContext = CommonContextProvider.CollectContext();
			Dictionary<string, object> value = new Dictionary<string, object>
			{
				{ "generalViewInfo", list },
				{ "selectedObjects", list2 },
				{ "additionalInfo", "These are all the selected objects. You can access them by their Identifier.ID." },
				{ "commonContext", commonContext }
			};
			return JsonConvert.SerializeObject(value);
		}

		private static Dictionary<string, Dictionary<string, string>> ModelSerializeWrapper(object obj)
		{
			Dictionary<string, Dictionary<string, string>> dictionary = new Dictionary<string, Dictionary<string, string>>();
			ISerializer serializer = SerializerFactory.CreateSerializer(obj);
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary2 = serializer.SerializeProperties(obj, 2, "", null, IgnoreProperties);
			Dictionary<string, string> value = dictionary2[PropertyTypeEnum.READ_ONLY];
			Dictionary<string, string> value2 = dictionary2[PropertyTypeEnum.MODIFIABLE];
			Dictionary<string, string> value3 = dictionary2[PropertyTypeEnum.TEMPLATE];
			Dictionary<string, string> value4 = dictionary2[PropertyTypeEnum.USER_DEFINED];
			dictionary["objectInfo"] = new Dictionary<string, string>
			{
				{
					"Tekla Open API Class Name",
					obj.GetType().Name
				},
				{
					"Identifier.ID",
					(obj is ModelObject modelObject) ? modelObject.Identifier.ID.ToString() : ""
				}
			};
			dictionary["readOnly"] = value;
			dictionary["modifiable"] = value2;
			dictionary["template"] = value3;
			dictionary["userDefined"] = value4;
			return dictionary;
		}

		private static List<Dictionary<string, Dictionary<string, string>>> CollectPropertiesFromSelectedObjects(ModelObjectEnumerator selectedObjects)
		{
			List<Dictionary<string, Dictionary<string, string>>> list = new List<Dictionary<string, Dictionary<string, string>>>();
			while (selectedObjects.MoveNext())
			{
				ModelObject current = selectedObjects.Current;
				if (current != null)
				{
					Dictionary<string, Dictionary<string, string>> item = ModelSerializeWrapper(current);
					list.Add(item);
				}
			}
			return list;
		}
	}
}
