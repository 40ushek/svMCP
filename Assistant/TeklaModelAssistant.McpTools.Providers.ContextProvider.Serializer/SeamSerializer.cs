using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class SeamSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Seam)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Seam seam, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (seam == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(seam, maxDepth, prefix, visited, ignorePropList, filterPropList);
			Hashtable values = new Hashtable();
			if (seam.GetAllUserProperties(ref values))
			{
				foreach (DictionaryEntry item in values)
				{
					string key = (string.IsNullOrEmpty(prefix) ? item.Key.ToString() : (prefix + "." + item.Key.ToString()));
					dictionary[PropertyTypeEnum.USER_DEFINED][key] = item.Value?.ToString() ?? "null";
				}
			}
			ModelObject primaryObject = seam.GetPrimaryObject();
			ISerializer serializer = SerializerFactory.CreateSerializer(primaryObject);
			if (primaryObject != null)
			{
				string prefix2 = (string.IsNullOrEmpty(prefix) ? "PrimaryObject" : (prefix + ".PrimaryObject"));
				Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested = serializer.SerializeProperties(primaryObject, maxDepth - 1, prefix2, visited, ignorePropList, filterPropList);
				Dictionary<string, string> dictionary2 = GenericDataSerializer.FlattenProperties(nested);
				foreach (KeyValuePair<string, string> item2 in dictionary2)
				{
					dictionary[PropertyTypeEnum.READ_ONLY][item2.Key] = item2.Value;
				}
			}
			foreach (ModelObject secondaryObject in seam.GetSecondaryObjects())
			{
				int num = 0;
				string prefix3 = (string.IsNullOrEmpty(prefix) ? $"Seam[{num}]" : $"{prefix}.Seam[{num}]");
				Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested2 = serializer.SerializeProperties(secondaryObject, maxDepth - 1, prefix3, visited, ignorePropList, filterPropList);
				Dictionary<string, string> dictionary3 = GenericDataSerializer.FlattenProperties(nested2);
				foreach (KeyValuePair<string, string> item3 in dictionary3)
				{
					dictionary[PropertyTypeEnum.READ_ONLY][item3.Key] = item3.Value;
				}
				num++;
			}
			return dictionary;
		}
	}
}
