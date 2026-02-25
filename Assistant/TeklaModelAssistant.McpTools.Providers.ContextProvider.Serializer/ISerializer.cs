using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	public interface ISerializer
	{
		Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null);
	}
}
