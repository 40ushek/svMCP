using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public static class PropertyAccessHelper
	{
		private enum PropertyOperation
		{
			Get,
			Set
		}

		private static readonly Dictionary<Type, PropertyInfo[]> TypePropertiesCache = new Dictionary<Type, PropertyInfo[]>();

		public static bool TryGetPropertyValue(Tekla.Structures.Model.ModelObject modelObject, string propertyPath, out object value)
		{
			object result = null;
			bool success = AccessProperty(modelObject, propertyPath, PropertyOperation.Get, ref result);
			if (!success && modelObject is Tekla.Structures.Model.Assembly assembly)
			{
				Tekla.Structures.Model.ModelObject mainPart = assembly.GetMainPart();
				if (mainPart != null)
				{
					success = AccessProperty(mainPart, propertyPath, PropertyOperation.Get, ref result);
				}
			}
			value = result;
			return success;
		}

		public static PropertyInfo TryGetPropertyInfo(Tekla.Structures.Model.ModelObject modelObject, string propertyPath)
		{
			if (modelObject == null || string.IsNullOrWhiteSpace(propertyPath))
			{
				return null;
			}
			(object ParentObject, PropertyInfo PropertyInfo) nestedProperty = GetNestedProperty(modelObject, propertyPath);
			var (parentObject, _) = nestedProperty;
			return nestedProperty.PropertyInfo;
		}

		public static bool TrySetPropertyValue(Tekla.Structures.Model.ModelObject modelObject, string propertyPath, object propertyValue)
		{
			bool success = AccessProperty(modelObject, propertyPath, PropertyOperation.Set, ref propertyValue);
			if (!success && modelObject is Tekla.Structures.Model.Assembly assembly)
			{
				Tekla.Structures.Model.ModelObject mainPart = assembly.GetMainPart();
				if (mainPart != null)
				{
					success = AccessProperty(mainPart, propertyPath, PropertyOperation.Set, ref propertyValue);
					if (success)
					{
						mainPart.Modify();
					}
				}
			}
			return success;
		}

		public static bool TrySetPropertyValue(DrawingObject drawingObject, string propertyPath, object propertyValue)
		{
			return AccessProperty(drawingObject, propertyPath, PropertyOperation.Set, ref propertyValue);
		}

		public static void CollectPropertyValues(Model model, IList<int> idsToProcess, string propertyName, Dictionary<int, string> propertyValues, List<int> notFoundIds, Dictionary<string, int> valueCounts)
		{
			foreach (int id in idsToProcess)
			{
				Tekla.Structures.Model.ModelObject modelObject = model.SelectModelObject(new Identifier(id));
				object value;
				if (modelObject == null)
				{
					notFoundIds.Add(id);
				}
				else if (TryGetPropertyValue(modelObject, propertyName, out value))
				{
					string stringValue = (propertyValues[id] = value?.ToString() ?? "(null)");
					string valueTrim = stringValue.Trim();
					if (valueCounts.ContainsKey(valueTrim))
					{
						valueCounts[valueTrim]++;
					}
					else
					{
						valueCounts[valueTrim] = 1;
					}
				}
				else
				{
					notFoundIds.Add(id);
				}
			}
		}

		private static bool AccessProperty(Tekla.Structures.Model.ModelObject modelObject, string propertyPath, PropertyOperation operation, ref object value)
		{
			if (modelObject == null || string.IsNullOrWhiteSpace(propertyPath))
			{
				return false;
			}
			var (parentObject, propInfo) = GetNestedProperty(modelObject, propertyPath);
			if (propInfo != null)
			{
				if (operation == PropertyOperation.Get)
				{
					value = propInfo.GetValue(parentObject);
					return true;
				}
				if (propInfo.CanWrite)
				{
					try
					{
						object convertedValue = ConvertPropertyValue(propInfo, value);
						propInfo.SetValue(parentObject, convertedValue, null);
						return true;
					}
					catch (InvalidCastException innerException)
					{
						throw new InvalidCastException($"Failed to convert value '{value}' to type '{propInfo.PropertyType.Name}' for property '{propertyPath}'.", innerException);
					}
					catch
					{
					}
				}
			}
			if (!propertyPath.Contains("."))
			{
				string udaValue = string.Empty;
				string udaPropertyPath = propertyPath.Trim().Replace(' ', '_').ToUpper();
				bool udaExist = modelObject.GetUserProperty(udaPropertyPath, ref udaValue);
				if (operation != PropertyOperation.Get)
				{
					return udaExist && modelObject.SetUserProperty(udaPropertyPath, value?.ToString() ?? string.Empty);
				}
				if (udaExist)
				{
					value = udaValue;
					return true;
				}
			}
			if (operation == PropertyOperation.Get)
			{
				string reportValue = string.Empty;
				if (modelObject.GetReportProperty(propertyPath, ref reportValue))
				{
					value = reportValue;
					return true;
				}
			}
			return false;
		}

		private static bool AccessProperty(DrawingObject drawingObject, string propertyPath, PropertyOperation operation, ref object value)
		{
			if (drawingObject == null || string.IsNullOrWhiteSpace(propertyPath))
			{
				return false;
			}
			var (parentObject, propInfo) = GetNestedProperty(drawingObject, propertyPath);
			if (propInfo != null)
			{
				if (operation == PropertyOperation.Get)
				{
					value = propInfo.GetValue(parentObject);
					return true;
				}
				if (propInfo.CanWrite)
				{
					try
					{
						object convertedValue = ConvertPropertyValue(propInfo, value);
						propInfo.SetValue(parentObject, convertedValue, null);
						return true;
					}
					catch (InvalidCastException innerException)
					{
						throw new InvalidCastException($"Failed to convert value '{value}' to type '{propInfo.PropertyType.Name}' for property '{propertyPath}'.", innerException);
					}
					catch
					{
					}
				}
			}
			if (!propertyPath.Contains("."))
			{
				if (operation != PropertyOperation.Get)
				{
					return drawingObject.SetUserProperty(propertyPath, value?.ToString() ?? string.Empty);
				}
				string udaValue = string.Empty;
				if (drawingObject.GetUserProperty(propertyPath, ref udaValue))
				{
					value = udaValue;
					return true;
				}
			}
			return false;
		}

		private static (object ParentObject, PropertyInfo PropertyInfo) GetNestedProperty(object obj, string propertyPath)
		{
			string[] propertyNames = propertyPath.Split('.');
			object currentObject = obj;
			for (int i = 0; i < propertyNames.Length - 1; i++)
			{
				PropertyInfo propInfo = GetPropertyInfo(currentObject, propertyNames[i]);
				if (propInfo == null)
				{
					return (ParentObject: null, PropertyInfo: null);
				}
				currentObject = propInfo.GetValue(currentObject);
				if (currentObject == null)
				{
					return (ParentObject: null, PropertyInfo: null);
				}
			}
			PropertyInfo finalPropInfo = GetPropertyInfo(currentObject, propertyNames.Last());
			return (ParentObject: currentObject, PropertyInfo: finalPropInfo);
		}

		private static PropertyInfo GetPropertyInfo(object obj, string propertyName)
		{
			if (obj == null)
			{
				return null;
			}
			Type type = obj.GetType();
			if (!TypePropertiesCache.TryGetValue(type, out var properties))
			{
				properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
				TypePropertiesCache[type] = properties;
			}
			return properties.FirstOrDefault((PropertyInfo p) => p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
		}

		public static object ConvertPropertyValue(PropertyInfo propInfo, object propertyValue)
		{
			if (propertyValue == null)
			{
				return null;
			}
			Type targetType = Nullable.GetUnderlyingType(propInfo.PropertyType) ?? propInfo.PropertyType;
			return ConvertPropertyValue(targetType, propertyValue);
		}

		public static object ConvertPropertyValue(Type targetType, object propertyValue)
		{
			if (targetType.IsEnum)
			{
				if (propertyValue is string stringValue)
				{
					return Enum.Parse(targetType, stringValue, true);
				}
				return Enum.ToObject(targetType, Convert.ChangeType(propertyValue, Enum.GetUnderlyingType(targetType)));
			}
			if (targetType == typeof(Point))
			{
				if (!propertyValue.ToString().TryParseToPoint(out var point))
				{
					throw new InvalidCastException($"Cannot convert value '{propertyValue}' to type 'Point'. Expected format: 'x,y,z'.");
				}
				return point;
			}
			return Convert.ChangeType(propertyValue, targetType);
		}
	}
}
