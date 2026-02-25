using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Tekla.Structures.Geometry3d;

namespace TeklaModelAssistant.McpTools.Providers.ContextProvider.Serializer
{
	internal class GenericDataSerializer : ISerializer
	{
		public static readonly Dictionary<Type, PropertyInfo[]> TypeProperties = new Dictionary<Type, PropertyInfo[]>();

		public Dictionary<PropertyTypeEnum, Dictionary<string, string>> SerializeProperties(object obj, int maxDepth = 2, string prefix = "", [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<object> visited = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> ignorePropList = null, [System.Runtime.CompilerServices.Nullable(new byte[] { 2, 0 })] HashSet<string> filterPropList = null)
		{
			Dictionary<PropertyTypeEnum, Dictionary<string, string>> dictionary = new Dictionary<PropertyTypeEnum, Dictionary<string, string>>
			{
				[PropertyTypeEnum.READ_ONLY] = new Dictionary<string, string>(),
				[PropertyTypeEnum.MODIFIABLE] = new Dictionary<string, string>(),
				[PropertyTypeEnum.TEMPLATE] = new Dictionary<string, string>(),
				[PropertyTypeEnum.USER_DEFINED] = new Dictionary<string, string>()
			};
			if (obj == null || maxDepth < 0)
			{
				return dictionary;
			}
			if (visited == null)
			{
				visited = new HashSet<object>();
			}
			if (visited.Contains(obj))
			{
				return dictionary;
			}
			visited.Add(obj);
			Type type = obj.GetType();
			if (!TypeProperties.TryGetValue(type, out var value))
			{
				value = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
				TypeProperties[type] = value;
			}
			PropertyInfo[] array = value;
			PropertyInfo[] array2 = array;
			foreach (PropertyInfo propertyInfo in array2)
			{
				string text = (string.IsNullOrEmpty(prefix) ? propertyInfo.Name : (prefix + "." + propertyInfo.Name));
				PropertyTypeEnum key;
				if (propertyInfo.CanRead && propertyInfo.CanWrite)
				{
					key = PropertyTypeEnum.MODIFIABLE;
				}
				else
				{
					if (!propertyInfo.CanRead || propertyInfo.CanWrite)
					{
						continue;
					}
					key = PropertyTypeEnum.READ_ONLY;
				}
				if ((ignorePropList != null && ShouldIgnore(text, ignorePropList)) || (filterPropList != null && !filterPropList.Contains(text)))
				{
					continue;
				}
				object obj2 = null;
				try
				{
					obj2 = propertyInfo.GetValue(obj, null);
				}
				catch
				{
					continue;
				}
				if (obj2 == null)
				{
					dictionary[key][text] = string.Empty;
					continue;
				}
				if (IsPrimitiveType(obj2))
				{
					if (IsFloatingPointType(obj2))
					{
						dictionary[key][text] = Convert.ToString(obj2, CultureInfo.InvariantCulture);
					}
					else
					{
						dictionary[key][text] = obj2.ToString();
					}
					continue;
				}
				if (obj2 is IEnumerable enumerable && !(obj2 is string))
				{
					int num = 0;
					foreach (object item in enumerable)
					{
						if (item == null)
						{
							dictionary[key][$"{text}[{num}]"] = string.Empty;
						}
						else if (IsPrimitiveType(item))
						{
							if (IsFloatingPointType(item))
							{
								dictionary[key][$"{text}[{num}]"] = Convert.ToString(item, CultureInfo.InvariantCulture);
							}
							else
							{
								dictionary[key][$"{text}[{num}]"] = item.ToString();
							}
						}
						else
						{
							ISerializer serializer = SerializerFactory.CreateSerializer(item);
							Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested = serializer.SerializeProperties(item, maxDepth - 1, $"{text}[{num}]", visited, ignorePropList, filterPropList);
							Dictionary<string, string> dictionary2 = FlattenProperties(nested);
							foreach (KeyValuePair<string, string> item2 in dictionary2)
							{
								dictionary[key][item2.Key] = item2.Value;
							}
						}
						num++;
					}
					continue;
				}
				ISerializer serializer2 = SerializerFactory.CreateSerializer(obj2);
				Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested2 = serializer2.SerializeProperties(obj2, maxDepth - 1, text, visited, ignorePropList, filterPropList);
				Dictionary<string, string> dictionary3 = FlattenProperties(nested2);
				foreach (KeyValuePair<string, string> item3 in dictionary3)
				{
					dictionary[key][item3.Key] = item3.Value;
				}
			}
			return dictionary;
		}

		public static Dictionary<string, string> FlattenProperties(Dictionary<PropertyTypeEnum, Dictionary<string, string>> nested)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			foreach (KeyValuePair<PropertyTypeEnum, Dictionary<string, string>> item in nested)
			{
				PropertyTypeEnum key = item.Key;
				foreach (KeyValuePair<string, string> item2 in item.Value)
				{
					dictionary[item2.Key] = item2.Value;
				}
			}
			return dictionary;
		}

		private static bool ShouldIgnore(string key, HashSet<string> ignorePropList)
		{
			foreach (string ignoreProp in ignorePropList)
			{
				if (key == ignoreProp || key.Contains(ignoreProp))
				{
					return true;
				}
			}
			return false;
		}

		private static bool IsPrimitiveType(object obj)
		{
			Type type = obj.GetType();
			return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(DateTime) || type == typeof(Guid) || type == typeof(Point) || type == typeof(Vector);
		}

		public static bool IsFloatingPointType(object obj)
		{
			Type type = obj.GetType();
			return type == typeof(double) || type == typeof(float) || type == typeof(decimal) || type == typeof(Point) || type == typeof(Vector);
		}
	}
}
