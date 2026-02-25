using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class DetailSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Detail)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Detail detail, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (detail == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(detail, maxDepth, prefix, visited, ignorePropList, filterPropList);
			Hashtable values = new Hashtable();
			if (detail.GetAllUserProperties(ref values))
			{
				foreach (DictionaryEntry item in values)
				{
					string key = (string.IsNullOrEmpty(prefix) ? item.Key.ToString() : (prefix + "." + item.Key.ToString()));
					if (item.Value == null)
					{
						dictionary[PropertyTypeEnum.USER_DEFINED][key] = "null";
					}
					else if (GenericDataSerializer.IsFloatingPointType(item.Value))
					{
						dictionary[PropertyTypeEnum.USER_DEFINED][key] = Convert.ToString(item.Value, CultureInfo.InvariantCulture);
					}
					else
					{
						dictionary[PropertyTypeEnum.USER_DEFINED][key] = item.Value.ToString();
					}
				}
			}
			ModelObject primaryObject = detail.GetPrimaryObject();
			if (primaryObject != null)
			{
				ISerializer serializer = SerializerFactory.CreateSerializer(primaryObject);
				string prefix2 = (string.IsNullOrEmpty(prefix) ? "PrimaryObject" : (prefix + ".PrimaryObject"));
				Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested = serializer.SerializeProperties(primaryObject, maxDepth - 1, prefix2, visited, ignorePropList, filterPropList);
				Dictionary<string, string> dictionary2 = GenericDataSerializer.FlattenProperties(nested);
				foreach (KeyValuePair<string, string> item2 in dictionary2)
				{
					dictionary[PropertyTypeEnum.READ_ONLY][item2.Key] = item2.Value;
				}
			}
			Point referencePoint = detail.GetReferencePoint();
			if (referencePoint != null)
			{
				string key2 = (string.IsNullOrEmpty(prefix) ? "ReferencePoint" : (prefix + ".ReferencePoint"));
				dictionary[PropertyTypeEnum.TEMPLATE][key2] = Convert.ToString(referencePoint, CultureInfo.InvariantCulture);
			}
			return dictionary;
		}
	}
}
