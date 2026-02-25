using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Collaboration;
using TeklaModelAssistant.McpTools.Extensions;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class ReferenceModelObjectSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((ReferenceModelObject)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		private Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(ReferenceModelObject rmObject, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (rmObject == null || maxDepth < 0)
			{
				return new Dictionary<PropertyTypeEnum, Dictionary<string, string>>
				{
					[PropertyTypeEnum.READ_ONLY] = new Dictionary<string, string>(),
					[PropertyTypeEnum.MODIFIABLE] = new Dictionary<string, string>(),
					[PropertyTypeEnum.TEMPLATE] = new Dictionary<string, string>(),
					[PropertyTypeEnum.USER_DEFINED] = new Dictionary<string, string>()
				};
			}
			GenericDataSerializer genericDataSerializer = new GenericDataSerializer();
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(rmObject, maxDepth, prefix, visited, ignorePropList, filterPropList);
			double value = 0.0;
			double value2 = 0.0;
			double value3 = 0.0;
			double value4 = 0.0;
			double value5 = 0.0;
			double value6 = 0.0;
			rmObject.GetReportProperty("BOUNDING_BOX_MIN_X", ref value);
			rmObject.GetReportProperty("BOUNDING_BOX_MIN_Y", ref value2);
			rmObject.GetReportProperty("BOUNDING_BOX_MIN_Z", ref value3);
			rmObject.GetReportProperty("BOUNDING_BOX_MAX_X", ref value4);
			rmObject.GetReportProperty("BOUNDING_BOX_MAX_Y", ref value5);
			rmObject.GetReportProperty("BOUNDING_BOX_MAX_Z", ref value6);
			string text = (string.IsNullOrEmpty(prefix) ? "" : (prefix + "."));
			dictionary[PropertyTypeEnum.READ_ONLY][text + "Bounding Box Min"] = new Point(value, value2, value3).ConvertToString();
			dictionary[PropertyTypeEnum.READ_ONLY][text + "Bounding Box Max"] = new Point(value4, value5, value6).ConvertToString();
			ReferenceModelObjectAttributeEnumerator referenceModelObjectAttributeEnumerator = new ReferenceModelObjectAttributeEnumerator(rmObject);
			ISerializer serializer = SerializerFactory.CreateSerializer((ReferenceModelObjectAttribute)referenceModelObjectAttributeEnumerator.Current);
			while (referenceModelObjectAttributeEnumerator.MoveNext())
			{
				ReferenceModelObjectAttribute obj = (ReferenceModelObjectAttribute)referenceModelObjectAttributeEnumerator.Current;
				string prefix2 = (string.IsNullOrEmpty(prefix) ? "Attribute" : (prefix + ".Attribute"));
				Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested = serializer.SerializeProperties(obj, maxDepth - 1, prefix2, visited, ignorePropList, filterPropList);
				Dictionary<string, string> dictionary2 = GenericDataSerializer.FlattenProperties(nested);
				foreach (KeyValuePair<string, string> item in dictionary2)
				{
					dictionary[PropertyTypeEnum.READ_ONLY][item.Key] = item.Value;
				}
			}
			return dictionary;
		}
	}
}
