using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class ContourSerializer : ISerializer
	{
		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			return Serialize((Contour)obj, maxDepth, prefix, visited, ignorePropList, filterPropList);
		}

		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> Serialize(Contour contour, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			if (contour == null || maxDepth < 0)
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
			List<string> list = new List<string>();
			foreach (ContourPoint contourPoint in contour.ContourPoints)
			{
				string text = FormattableString.Invariant(FormattableStringFactory.Create("({0}; {1}; {2})", contourPoint.X.ToString("F2"), contourPoint.Y.ToString("F2"), contourPoint.Z.ToString("F2")));
				if (contourPoint.Chamfer.Type != Chamfer.ChamferTypeEnum.CHAMFER_NONE)
				{
					string text2 = FormattableString.Invariant($"Chamfer: (Type: {contourPoint.Chamfer.Type.ToString()}, X:{contourPoint.Chamfer.X.ToString()}, Y:{contourPoint.Chamfer.Y.ToString()}, DZ1:{contourPoint.Chamfer.DZ1.ToString()},  DZ2: {contourPoint.Chamfer.DZ2.ToString()})");
					text = text + " " + text2;
				}
				list.Add(text);
			}
			string key = (string.IsNullOrEmpty(prefix) ? "ContourPoints" : (prefix + ".ContourPoints"));
			string value = string.Join(", ", list);
			dictionary[PropertyTypeEnum.MODIFIABLE][key] = value;
			return dictionary;
		}
	}
}
