using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class AssemblySerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Assembly)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		private Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Assembly assembly, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (assembly == null || maxDepth < 0)
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
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = genericDataSerializer.SerializeProperties(assembly, maxDepth, prefix, visited, ignorePropList, filterPropList);
			ModelObject mainPart = assembly.GetMainPart();
			if (mainPart is Part part)
			{
				string prefix2 = (string.IsNullOrEmpty(prefix) ? "Main part" : (prefix + ".Main part"));
				getAssemblyPartProperties(part, prefix2, dictionary);
			}
			ArrayList secondaries = assembly.GetSecondaries();
			int num = 1;
			int num2 = 1;
			foreach (ModelObject item in secondaries)
			{
				if (item is Part part2)
				{
					string prefix3 = (string.IsNullOrEmpty(prefix) ? $"Secondary part {num}" : $"{prefix}.Secondary part {num}");
					getAssemblyPartProperties(part2, prefix3, dictionary);
					num++;
				}
			}
			foreach (ModelObject subAssembly in assembly.GetSubAssemblies())
			{
				if (subAssembly is Assembly assembly2)
				{
					string prefix4 = (string.IsNullOrEmpty(prefix) ? $"Sub assembly {num2}" : $"{prefix}.Sub assembly {num2}");
					getSubAssemblyProperties(assembly2, prefix4, dictionary);
					num2++;
				}
			}
			return dictionary;
		}

		private void getAssemblyPartProperties(Part part, string prefix, Dictionary<PropertyTypeEnum, Dictionary<string, string>> properties)
		{
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Name"] = part.Name;
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".ID"] = part.Identifier.ID.ToString();
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Type"] = part.GetType().ToString();
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Profile"] = part.Profile.ProfileString;
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Solid.MinimumPoint"] = part.GetSolid().MinimumPoint.ConvertToString();
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Solid.MaximumPoint"] = part.GetSolid().MaximumPoint.ConvertToString();
		}

		private void getSubAssemblyProperties(Assembly assembly, string prefix, Dictionary<PropertyTypeEnum, Dictionary<string, string>> properties)
		{
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Name"] = assembly.Name;
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".ID"] = assembly.Identifier.ID.ToString();
			properties[PropertyTypeEnum.READ_ONLY][prefix + ".Type"] = assembly.GetType().ToString();
		}
	}
}
