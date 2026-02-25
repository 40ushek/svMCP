using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class ConnectionSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Connection)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Connection connection, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (connection == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(connection, maxDepth, prefix, visited, ignorePropList, filterPropList);
			string text = (string.IsNullOrEmpty(prefix) ? "" : (prefix + "."));
			ModelObject primaryObject = connection.GetPrimaryObject();
			if (primaryObject is Part part)
			{
				dictionary[PropertyTypeEnum.READ_ONLY][text + "Main Part Name"] = part.Name;
				dictionary[PropertyTypeEnum.READ_ONLY][text + "Main Part ID"] = part.Identifier.ID.ToString();
				dictionary[PropertyTypeEnum.READ_ONLY][text + "Main Part Profile"] = part.Profile.ProfileString;
			}
			ArrayList secondaryObjects = connection.GetSecondaryObjects();
			int num = 1;
			foreach (ModelObject item in secondaryObjects)
			{
				if (item is Part part2)
				{
					dictionary[PropertyTypeEnum.READ_ONLY][$"{text}Secondary Part {num} Name"] = part2.Name;
					dictionary[PropertyTypeEnum.READ_ONLY][$"{text}Secondary Part {num} ID"] = part2.Identifier.ID.ToString();
					dictionary[PropertyTypeEnum.READ_ONLY][$"{text}Secondary Part {num} Profile"] = part2.Profile.ProfileString;
					num++;
				}
			}
			Hashtable values = new Hashtable();
			if (connection.GetAllUserProperties(ref values))
			{
				foreach (DictionaryEntry item2 in values)
				{
					string key = (string.IsNullOrEmpty(prefix) ? item2.Key.ToString() : (prefix + "." + item2.Key.ToString()));
					dictionary[PropertyTypeEnum.READ_ONLY][key] = item2.Value?.ToString() ?? "null";
				}
			}
			return dictionary;
		}
	}
}
