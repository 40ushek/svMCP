using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class BoltArraySerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((BoltArray)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		private Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(BoltArray bolt, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (bolt == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(bolt, maxDepth, prefix, visited, ignorePropList, filterPropList);
			string text = "";
			for (int i = 0; i < bolt.GetBoltDistXCount(); i++)
			{
				text = text + bolt.GetBoltDistX(i).ToString(CultureInfo.InvariantCulture) + " ";
			}
			string text2 = "";
			for (int j = 0; j < bolt.GetBoltDistYCount(); j++)
			{
				text2 = text2 + bolt.GetBoltDistY(j).ToString(CultureInfo.InvariantCulture) + " ";
			}
			string text3 = (string.IsNullOrEmpty(prefix) ? "" : (prefix + "."));
			dictionary[PropertyTypeEnum.MODIFIABLE][text3 + "Bolt Dist X"] = text.Trim();
			dictionary[PropertyTypeEnum.MODIFIABLE][text3 + "Bolt Dist Y"] = text2.Trim();
			return dictionary;
		}
	}
}
