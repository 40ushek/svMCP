using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaModelAssistant.McpTools.Extensions;
using TeklaModelAssistant.McpTools.Helpers;
using TeklaModelAssistant.McpTools.Models;

namespace TeklaModelAssistant.McpTools.Tools
{
	[Description("Tekla Structures tool to create model objects.")]
	public class TeklaCreateModelObjectsTool
	{
		private const int MaxModelObjectsPerCall = 50;

		private const string StandardAttributeFileName = "standard";

		private static readonly string[] ColumnTypeExtensions = new string[3] { ".clm", ".ccl", ".cpf" };

		private static readonly Dictionary<string, Func<ModelObject>> _objectCreatorFunctions = BuildCreatorMap();

		private static Dictionary<string, Func<ModelObject>> BuildCreatorMap()
		{
			Dictionary<string, Func<ModelObject>> map = new Dictionary<string, Func<ModelObject>>();
			Add(new string[4] { ".prt", ".crs", ".dia", ".cbm" }, () => new Beam());
			Add(new string[2] { ".clm", ".ccl" }, () => new Beam(Beam.BeamTypeEnum.COLUMN));
			Add(new string[3] { ".cpl", ".lsl", ".lpl" }, () => new ContourPlate());
			Add(new string[2] { ".bpl", ".fpl" }, () => new BentPlate());
			Add(new string[1] { ".ply" }, () => new PolyBeam());
			Add(new string[1] { ".cpn" }, () => new Beam(Beam.BeamTypeEnum.PANEL));
			Add(new string[1] { ".cpf" }, () => new Beam(Beam.BeamTypeEnum.PAD_FOOTING));
			Add(new string[1] { ".csf" }, () => new Beam(Beam.BeamTypeEnum.STRIP_FOOTING));
			return map;
			void Add(string[] extensions, Func<ModelObject> action)
			{
				foreach (string ext in extensions)
				{
					map[ext] = action;
				}
			}
		}

		[Description("Creates multiple objects like Parts (Beam, PolyBeam, etc.), Grids, or Connections. Tool only supports creation of objects of the same type.Follow object creation chunks for detailed logic (grid intersections, steel frame connections, etc.).")]
		public static ToolExecutionResult CreateModelObjects([Description("Property sets of the objects to be created (e.g., Object.Type, StartPoint, EndPoint, CoordinateX). It is a list of dictionaries in JSON format. Key is the property name, value is its value.The number of property sets defines the number of objects to be created (max 50 entries).CRITICAL: Do NOT include optional properties (Name, Profile, Material, Class, etc.) in the JSON unless the user explicitly requests them as overrides.IMPORTANT FORMATTING: Single points (StartPoint/EndPoint) must be strings like \"(X,Y,Z)\". Point lists (ContourPoints) must be a single string like \"(X1,Y1,Z1);(X2,Y2,Z2);...\".")] string propertySetsListString, [Description("MANDATORY: The file extension (including dot) determines default properties. MUST be one of: Steel Parts: .prt (Beam), .clm (Column), .crs (Ortho Beam), .dia (Twin Profile), .cpl (Contour Plate), .bpl (Bent Plate), .fpl (Folded Plate), .lpl (Lofted Plate). Concrete Parts: .cbm (Beam), .ccl (Column), .lsl (Lofted Slab), .cpn (Panel). Footings: .cpf (Pad Footing), .csf (Strip Footing). ")] string fileExtension)
		{
			if (!propertySetsListString.TryConvertFromJson<List<Dictionary<string, string>>>(out var propertySets))
			{
				return ToolExecutionResult.CreateErrorResult("Failed to parse propertySetsListString. Ensure it is a valid JSON dictionary list.");
			}
			if (string.IsNullOrWhiteSpace(fileExtension))
			{
				return ToolExecutionResult.CreateErrorResult("The 'fileExtension' argument must be provided and non-empty.");
			}
			if (propertySets.Count > 50)
			{
				return ToolExecutionResult.CreateErrorResult($"Too many model objects in one call. Maximum is {50}, received {propertySets.Count}. Split into multiple calls.");
			}
			Model model = new Model();
			List<object> createdModelObjects = new List<object>();
			List<object> failedModelObjects = new List<object>();
			for (int i = 0; i < propertySets.Count; i++)
			{
				if (!TryCreatePart(model, propertySets[i], fileExtension, out var part, out var errorMessage))
				{
					failedModelObjects.Add(new
					{
						Index = i,
						Error = errorMessage
					});
				}
				else
				{
					createdModelObjects.Add(new
					{
						Index = i,
						PartId = part.Identifier.ToString(),
						Warning = errorMessage
					});
				}
			}
			if (createdModelObjects.Count == 0)
			{
				return ToolExecutionResult.CreateErrorResult("No model objects were created.", null, failedModelObjects);
			}
			model.CommitChanges("(TMA) CreateModelObjects");
			return ToolExecutionResult.CreateSuccessResult($"Created {createdModelObjects.Count} out of {propertySets.Count} model objects.", new
			{
				CreatedObjects = createdModelObjects,
				FailedObjects = failedModelObjects
			});
		}

		private static bool TryCreatePart(Model model, Dictionary<string, string> properties, string fileExtension, out Part part, out string errorMessage)
		{
			part = null;
			try
			{
				if (!_objectCreatorFunctions.TryGetValue(fileExtension, out var objectCreatorFunction))
				{
					errorMessage = "Unsupported part file extension " + fileExtension + ".";
					return false;
				}
				part = objectCreatorFunction() as Part;
				if (part == null)
				{
					errorMessage = "Part type " + fileExtension + " could not be instantiated.";
					return false;
				}
				if (!string.IsNullOrEmpty(fileExtension))
				{
					part.LoadPartAttributesFromFile("standard", fileExtension);
				}
				Beam beam = part as Beam;
				double standardHeight = 0.0;
				bool useStandardHeightBool = default(bool);
				if (beam != null && ColumnTypeExtensions.Contains(fileExtension) && properties.TryGetValue("UseStandardHeight", out var useStandardHeight) && bool.TryParse(useStandardHeight, out useStandardHeightBool) && useStandardHeightBool)
				{
					standardHeight = Math.Abs(beam.EndPoint.Z - beam.StartPoint.Z);
				}
				StringBuilder messageBuilder = new StringBuilder();
				foreach (KeyValuePair<string, string> kvp in properties)
				{
					try
					{
						if (!HandlePartSpecialProperties(part, kvp.Key, kvp.Value) && !PropertyAccessHelper.TrySetPropertyValue(part, kvp.Key, kvp.Value))
						{
							messageBuilder.Append(CreateNotSetPropertyNote(kvp.Key));
						}
					}
					catch (Exception exception)
					{
						messageBuilder.Append(CreateNotSetPropertyNote(kvp.Key, exception));
					}
				}
				if (standardHeight != 0.0)
				{
					beam.EndPoint = new Point(beam.StartPoint)
					{
						Z = beam.StartPoint.Z + standardHeight
					};
				}
				if (!part.Insert())
				{
					errorMessage = "Part of type " + fileExtension + " could not be inserted into the model.";
					return false;
				}
				errorMessage = messageBuilder.ToString();
				return true;
			}
			catch (Exception ex)
			{
				errorMessage = "Error creating part of type " + fileExtension + ": " + ex.Message;
				return false;
			}
		}

		private static string CreateNotSetPropertyNote(string propertyName, Exception exception = null)
		{
			string message = "Note: Property '" + propertyName + "' was not set.";
			if (exception != null)
			{
				message += exception.Message;
			}
			return message;
		}

		private static bool HandlePartSpecialProperties(Part part, string propName, string value)
		{
			if (propName == "Object.Type")
			{
				return true;
			}
			if (propName == "UseStandardHeight")
			{
				return true;
			}
			if (part is Beam beam && (propName == "StartPoint" || propName == "EndPoint"))
			{
				Point jsonPoint = PropertyAccessHelper.ConvertPropertyValue(typeof(Point), value) as Point;
				if (jsonPoint == null)
				{
					return false;
				}
				if (propName == "StartPoint")
				{
					beam.StartPoint = jsonPoint;
				}
				else
				{
					beam.EndPoint = jsonPoint;
				}
				return true;
			}
			if (propName == "ContourPoints")
			{
				IEnumerable<Point> points = from pStr in value.Split(';')
					select PropertyAccessHelper.ConvertPropertyValue(typeof(Point), pStr) as Point;
				foreach (Point point in points)
				{
					if (point == null)
					{
						continue;
					}
					if (part is ContourPlate cp)
					{
						cp.AddContourPoint(new ContourPoint(point, null));
						continue;
					}
					if (part is PolyBeam pb)
					{
						pb.AddContourPoint(new ContourPoint(point, null));
						continue;
					}
					throw new NotImplementedException("ContourPoints are not supported for part type " + part.GetType().Name);
				}
				return true;
			}
			return false;
		}
	}
}
