using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tekla.Structures;
using Tekla.Structures.Dialog.UIControls;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public static class TsAttributeFiles
	{
		public static List<string> GetTeklaSettingFiles(string fileExtension)
		{
			List<string> propertyDirectories = EnvironmentFiles.GetStandardPropertyFileDirectories();
			return EnvironmentFiles.GetMultiDirectoryFileList(propertyDirectories, fileExtension);
		}

		public static void LoadPartAttributesFromFile(this Part part, string attributeFile, string fileExtension)
		{
			if (string.IsNullOrEmpty(attributeFile))
			{
				return;
			}
			string fileName = attributeFile + fileExtension;
			ModelInfo modelInfo = new Model().GetInfo();
			TeklaStructuresFiles allFiles = new TeklaStructuresFiles((modelInfo != null) ? modelInfo.ModelPath : string.Empty);
			FileInfo fileInfo = allFiles.GetAttributeFile(fileName);
			if (fileInfo == null)
			{
				return;
			}
			using (StreamReader reader = new StreamReader(fileInfo.FullName))
			{
				string buffer = null;
				while ((buffer = reader.ReadLine()) != null)
				{
					int pos = buffer.IndexOf(' ');
					if (pos > 0)
					{
						string property = buffer.Substring(0, pos);
						if (property.StartsWith("dia_part_attr."))
						{
							property = property.Substring(14);
						}
						if (property.StartsWith("part_attributes."))
						{
							property = property.Substring(16);
						}
						string value = buffer.Substring(pos + 1).Trim('"');
						SetPartDialogProperty(part, property, value);
					}
				}
			}
		}

		private static void SetPartDialogProperty(Part part, string dialogPropertyName, string propertyValue)
		{
			int number = 0;
			double doubleVal = 0.0;
			switch (dialogPropertyName)
			{
			case "name":
				part.Name = propertyValue;
				break;
			case "profile":
				part.Profile.ProfileString = propertyValue;
				break;
			case "material":
				part.Material.MaterialString = propertyValue;
				break;
			case "finish":
				part.Finish = propertyValue;
				break;
			case "part_group":
				part.Class = propertyValue;
				break;
			case "part_number_prefix":
				part.PartNumber.Prefix = propertyValue;
				break;
			case "assembly_number_prefix":
				part.AssemblyNumber.Prefix = propertyValue;
				break;
			case "part_number_start_no":
				if (int.TryParse(propertyValue, out number))
				{
					part.PartNumber.StartNumber = number;
				}
				break;
			case "assembly_number_start_no":
				if (int.TryParse(propertyValue, out number))
				{
					part.AssemblyNumber.StartNumber = number;
				}
				break;
			case "position_plane":
				if (int.TryParse(propertyValue, out number))
				{
					part.Position.Plane = (Position.PlaneEnum)number;
				}
				break;
			case "position_depth":
				if (int.TryParse(propertyValue, out number))
				{
					part.Position.Depth = (Position.DepthEnum)number;
				}
				break;
			case "rotation":
				if (int.TryParse(propertyValue, out number))
				{
					part.Position.Rotation = (Position.RotationEnum)number;
				}
				break;
			case "value_position_plane":
				if (double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					part.Position.PlaneOffset = doubleVal;
				}
				break;
			case "value_position_depth":
				if (double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					part.Position.DepthOffset = doubleVal;
				}
				break;
			case "value_rotation":
				if (double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					part.Position.RotationOffset = doubleVal;
				}
				break;
			case "dx1":
				if (part is Beam beamBottom && double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					beamBottom.StartPoint.Z = doubleVal;
				}
				break;
			case "dx2":
				if (part is Beam beamTop && double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					beamTop.EndPoint.Z = doubleVal;
				}
				break;
			case "Shortening":
				if (double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					part.DeformingData.Shortening = doubleVal;
				}
				break;
			case "Cambering":
				if (double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					part.DeformingData.Cambering = doubleVal;
				}
				break;
			case "WarpingAngle1":
				if (part is Beam beamWarp3 && double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					beamWarp3.DeformingData.Angle = doubleVal;
				}
				break;
			case "WarpingAngle2":
				if (part is Beam beamWarp2 && double.TryParse(propertyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleVal))
				{
					beamWarp2.DeformingData.Angle2 = doubleVal;
				}
				break;
			case "PourPhase":
				if (int.TryParse(propertyValue, out number))
				{
					part.PourPhase = number;
				}
				break;
			case "AssemblyType":
				part.CastUnitType = ((!(propertyValue == "0")) ? Part.CastUnitTypeEnum.CAST_IN_PLACE : Part.CastUnitTypeEnum.PRECAST);
				break;
			}
		}
	}
}
