using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class BeamSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Beam)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		private Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Beam beam, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (beam == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(beam, maxDepth, prefix, visited, ignorePropList, filterPropList);
			double value = 0.0;
			double value2 = 0.0;
			beam.GetReportProperty("HEIGHT", ref value);
			beam.GetReportProperty("WIDTH", ref value2);
			string text = (string.IsNullOrEmpty(prefix) ? "" : (prefix + "."));
			dictionary[PropertyTypeEnum.TEMPLATE][text + "HEIGHT (Profile)"] = value.ToString(CultureInfo.InvariantCulture);
			dictionary[PropertyTypeEnum.TEMPLATE][text + "WIDTH (Profile)"] = value2.ToString(CultureInfo.InvariantCulture);
			return dictionary;
		}
	}
}
