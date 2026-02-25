using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class ComponentSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Component)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		private Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Component component, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (component == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(component, maxDepth, prefix, visited, ignorePropList, filterPropList);
			Hashtable values = new Hashtable();
			if (component.GetAllUserProperties(ref values))
			{
				foreach (DictionaryEntry item in values)
				{
					string key = (string.IsNullOrEmpty(prefix) ? (item.Key.ToString() ?? "") : (prefix + "." + item.Key.ToString()));
					dictionary[PropertyTypeEnum.USER_DEFINED][key] = item.Value?.ToString() ?? "null";
				}
			}
			return dictionary;
		}
	}
}
