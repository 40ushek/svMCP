using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class OffsetSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Offset)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Offset offset, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (offset == null || maxDepth < 0)
			{
				return new Dictionary<PropertyTypeEnum, Dictionary<string, string>>
				{
					[PropertyTypeEnum.READ_ONLY] = new Dictionary<string, string>(),
					[PropertyTypeEnum.MODIFIABLE] = new Dictionary<string, string>(),
					[PropertyTypeEnum.TEMPLATE] = new Dictionary<string, string>(),
					[PropertyTypeEnum.USER_DEFINED] = new Dictionary<string, string>()
				};
			}
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = new Dictionary<PropertyTypeEnum, Dictionary<string, string>> { [PropertyTypeEnum.MODIFIABLE] = new Dictionary<string, string>() };
			string key = (string.IsNullOrEmpty(prefix) ? "Offset" : (prefix + ".Offset"));
			dictionary[PropertyTypeEnum.MODIFIABLE].Add(key, FormattableString.Invariant(FormattableStringFactory.Create("({0}; {1}; {2})", offset.Dx.ToString("F2"), offset.Dy.ToString("F2"), offset.Dz.ToString("F2"))));
			return dictionary;
		}
	}
}
