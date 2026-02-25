using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Tekla.Structures;
using Tekla.Structures.Filtering;
using Tekla.Structures.Filtering.Categories;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class FilterHelper
	{
		public static BinaryFilterExpressionCollection BuildFilterExpressionsWithParentheses(string filterCriteria)
		{
			if (string.IsNullOrWhiteSpace(filterCriteria))
			{
				throw new ArgumentException("Filter criteria cannot be null or empty.", "filterCriteria");
			}
			if (!filterCriteria.Contains("(") && !filterCriteria.Contains(")"))
			{
				return BuildFilterExpressionsFromArray(ConvertFilterStringToJArray(filterCriteria));
			}
			FilterTokenizer tokenizer = new FilterTokenizer();
			List<Token> tokens = tokenizer.Tokenize(filterCriteria);
			FilterExpressionParser parser = new FilterExpressionParser();
			FilterNode ast = parser.Parse(tokens);
			FilterAstBuilder builder = new FilterAstBuilder();
			return builder.BuildFromAst(ast);
		}

		public static BinaryFilterExpression CreateFilterExpressionPublic(string category, string property, string operatorStr, string value)
		{
			return CreateFilterExpression(category, property, operatorStr, value);
		}

		public static JsonArray ConvertFilterStringToJArray(string filterCriteria)
		{
			JsonArray expressionsArray = new JsonArray();
			string[] ruleStrings = filterCriteria.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			string[] array = ruleStrings;
			foreach (string ruleString in array)
			{
				string[] components = (from s in ruleString.Split('|')
					select s.Trim()).ToArray();
				if (components.Length >= 4)
				{
					JsonObject expressionObject = new JsonObject
					{
						["category"] = components[0],
						["property"] = components[1],
						["operator"] = components[2],
						["value"] = components[3]
					};
					if (components.Length > 4)
					{
						expressionObject["logicalOperator"] = components[4];
					}
					expressionsArray.Add(expressionObject);
				}
			}
			return expressionsArray;
		}

		public static BinaryFilterExpressionCollection BuildFilterExpressionsFromArray(object expressionsArgument)
		{
			BinaryFilterExpressionCollection expressions = new BinaryFilterExpressionCollection();
			List<Dictionary<string, object>> expressionList = new List<Dictionary<string, object>>();
			JsonArray jsonArray = null;
			if (expressionsArgument is string jsonString)
			{
				jsonArray = JsonNode.Parse(jsonString)?.AsArray();
			}
			else if (expressionsArgument is JsonArray arr)
			{
				jsonArray = arr;
			}
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (!(item is JsonObject jObject))
					{
						continue;
					}
					Dictionary<string, object> expressionDict = new Dictionary<string, object>();
					foreach (KeyValuePair<string, JsonNode> prop in jObject)
					{
						expressionDict[prop.Key] = prop.Value?.ToString();
					}
					expressionList.Add(expressionDict);
				}
			}
			else if (expressionsArgument is IEnumerable enumerable)
			{
				foreach (object item2 in enumerable)
				{
					if (item2 is Dictionary<string, object> dict)
					{
						expressionList.Add(dict);
					}
				}
			}
			for (int i = 0; i < expressionList.Count; i++)
			{
				Dictionary<string, object> expression = expressionList[i];
				if (!expression.ContainsKey("category") || !expression.ContainsKey("property") || !expression.ContainsKey("operator") || !expression.ContainsKey("value"))
				{
					List<string> missingKeys = new List<string>();
					if (!expression.ContainsKey("category"))
					{
						missingKeys.Add("category");
					}
					if (!expression.ContainsKey("property"))
					{
						missingKeys.Add("property");
					}
					if (!expression.ContainsKey("operator"))
					{
						missingKeys.Add("operator");
					}
					if (!expression.ContainsKey("value"))
					{
						missingKeys.Add("value");
					}
					string errorMessage = string.Format("Invalid filter expression at index {0}: missing required keys: {1}", i, string.Join(", ", missingKeys));
					throw new FilterExpressionException(errorMessage);
				}
				string category = expression["category"].ToString();
				string property = expression["property"].ToString();
				string operatorStr = expression["operator"].ToString();
				string value = expression["value"].ToString();
				BinaryFilterOperatorType logicalOp = BinaryFilterOperatorType.BOOLEAN_AND;
				if (i > 0 && expression.ContainsKey("logicalOperator"))
				{
					string logicalOpStr = expression["logicalOperator"].ToString();
					logicalOp = ((!(logicalOpStr == "OR")) ? BinaryFilterOperatorType.BOOLEAN_AND : BinaryFilterOperatorType.BOOLEAN_OR);
				}
				BinaryFilterExpression filterExpression = CreateFilterExpression(category, property, operatorStr, value);
				if (filterExpression == null)
				{
					string errorMessage2 = $"Failed to create filter expression for category '{category}', property '{property}', operator '{operatorStr}', value '{value}' at index {i}";
					throw new FilterExpressionException(errorMessage2, category, property, operatorStr, value);
				}
				expressions.Add(new BinaryFilterExpressionItem(filterExpression, logicalOp));
			}
			return expressions;
		}

		private static BinaryFilterExpression CreateFilterExpression(string category, string property, string operatorStr, string value)
		{
			try
			{
				if (category == "Template")
				{
					if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
					{
						TemplateFilterExpressions.CustomNumber typeExpr = new TemplateFilterExpressions.CustomNumber(property);
						NumericConstantFilterExpression numValue = new NumericConstantFilterExpression(parsedValue);
						NumericOperatorType numOp = GetNumericOperator(operatorStr);
						return new BinaryFilterExpression(typeExpr, numOp, numValue);
					}
					TemplateFilterExpressions.CustomString templateExpr = new TemplateFilterExpressions.CustomString(property);
					StringConstantFilterExpression stringValue = new StringConstantFilterExpression(value);
					StringOperatorType stringOp = GetStringOperator(operatorStr);
					return new BinaryFilterExpression(templateExpr, stringOp, stringValue);
				}
				switch (category)
				{
				case "Object":
					return CreateObjectFilterExpression(property, operatorStr, value);
				case "Part":
					return CreatePartFilterExpression(property, operatorStr, value);
				case "Assembly":
					return CreateAssemblyFilterExpression(property, operatorStr, value);
				case "Component":
					return CreateComponentFilterExpression(property, operatorStr, value);
				case "Bolt":
					return CreateBoltFilterExpression(property, operatorStr, value);
				case "ReinforcingBar":
					return CreateReinforcingBarFilterExpression(property, operatorStr, value);
				case "ReferenceObject":
					return CreateReferenceObjectFilterExpression(property, operatorStr, value);
				case "Surface":
					return CreateSurfaceFilterExpression(property, operatorStr, value);
				case "PourObject":
					return CreatePourObjectFilterExpression(property, operatorStr, value);
				case "PourUnit":
					return CreatePourUnitFilterExpression(property, operatorStr, value);
				case "ConstructionObject":
					return CreateConstructionObjectFilterExpression(property, operatorStr, value);
				case "Load":
					return CreateLoadFilterExpression(property, operatorStr, value);
				case "Weld":
					return CreateWeldFilterExpression(property, operatorStr, value);
				case "Location breakdown structure":
					return CreateLocationBreakdownStructureFilterExpression(property, operatorStr, value);
				default:
				{
					string errorMessage = "Unsupported filter category: '" + category + "'. Supported categories are: Object, Part, Assembly, Component, Bolt, ReinforcingBar, ReferenceObject, Surface, PourObject, PourUnit, ConstructionObject, Load, Weld, Location breakdown structure, Template";
					throw new FilterExpressionException(errorMessage, category, property, operatorStr, value);
				}
				}
			}
			catch (FilterExpressionException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				string errorMessage2 = "Error creating filter expression for category '" + category + "', property '" + property + "', operator '" + operatorStr + "', value '" + value + "': " + ex2.Message;
				throw new FilterExpressionException(errorMessage2, ex2, category, property, operatorStr, value);
			}
		}

		private static BinaryFilterExpression CreateObjectFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "object type":
			case "type":
			{
				ObjectFilterExpressions.Type typeExpr = new ObjectFilterExpressions.Type();
				NumericConstantFilterExpression numValue = new NumericConstantFilterExpression(GetObjectTypeNumericValue(value));
				NumericOperatorType numOp = GetNumericOperator(operatorStr);
				return new BinaryFilterExpression(typeExpr, numOp, numValue);
			}
			case "guid":
			{
				ObjectFilterExpressions.Guid guidExpr = new ObjectFilterExpressions.Guid();
				StringConstantFilterExpression guidValue = new StringConstantFilterExpression(value);
				StringOperatorType guidOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(guidExpr, guidOp, guidValue);
			}
			case "idnumber":
			case "id number":
				try
				{
					ObjectFilterExpressions.IdNumber idNumberExpr = new ObjectFilterExpressions.IdNumber();
					NumericConstantFilterExpression idNumberValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType idNumberOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(idNumberExpr, idNumberOp, idNumberValue);
				}
				catch (FormatException innerException3)
				{
					string idNumberErrorMessage = "Invalid numeric value '" + value + "' for Object.IdNumber property";
					throw new FilterExpressionException(idNumberErrorMessage, innerException3, "Object", property, operatorStr, value);
				}
			case "iscomponent":
			case "is component":
				try
				{
					ObjectFilterExpressions.IsComponent isComponentExpr = new ObjectFilterExpressions.IsComponent();
					NumericConstantFilterExpression isComponentValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType isComponentOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(isComponentExpr, isComponentOp, isComponentValue);
				}
				catch (FormatException innerException2)
				{
					string isComponentErrorMessage = "Invalid numeric value '" + value + "' for Object.IsComponent property";
					throw new FilterExpressionException(isComponentErrorMessage, innerException2, "Object", property, operatorStr, value);
				}
			case "phase":
				try
				{
					ObjectFilterExpressions.Phase phaseExpr = new ObjectFilterExpressions.Phase();
					NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
				}
				catch (FormatException innerException)
				{
					string phaseErrorMessage = "Invalid numeric value '" + value + "' for Object.Phase property";
					throw new FilterExpressionException(phaseErrorMessage, innerException, "Object", property, operatorStr, value);
				}
			default:
			{
				ObjectFilterExpressions.CustomString customExpr = new ObjectFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreatePartFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "name":
			{
				PartFilterExpressions.Name nameExpr = new PartFilterExpressions.Name();
				StringConstantFilterExpression nameValue = new StringConstantFilterExpression(value);
				StringOperatorType nameOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(nameExpr, nameOp, nameValue);
			}
			case "class":
				try
				{
					PartFilterExpressions.Class classExpr = new PartFilterExpressions.Class();
					NumericConstantFilterExpression classValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType classOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(classExpr, classOp, classValue);
				}
				catch (FormatException innerException5)
				{
					string classErrorMessage = "Invalid numeric value '" + value + "' for Part.Class property";
					throw new FilterExpressionException(classErrorMessage, innerException5, "Part", property, operatorStr, value);
				}
			case "profile":
			{
				PartFilterExpressions.Profile profileExpr = new PartFilterExpressions.Profile();
				StringConstantFilterExpression profileValue = new StringConstantFilterExpression(value);
				StringOperatorType profileOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(profileExpr, profileOp, profileValue);
			}
			case "material":
			{
				PartFilterExpressions.Material materialExpr = new PartFilterExpressions.Material();
				StringConstantFilterExpression materialValue = new StringConstantFilterExpression(value);
				StringOperatorType materialOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(materialExpr, materialOp, materialValue);
			}
			case "finish":
			{
				PartFilterExpressions.Finish finishExpr = new PartFilterExpressions.Finish();
				StringConstantFilterExpression finishValue = new StringConstantFilterExpression(value);
				StringOperatorType finishOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(finishExpr, finishOp, finishValue);
			}
			case "lot":
				try
				{
					PartFilterExpressions.Lot lotExpr = new PartFilterExpressions.Lot();
					NumericConstantFilterExpression lotValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType lotOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(lotExpr, lotOp, lotValue);
				}
				catch (FormatException innerException4)
				{
					string lotErrorMessage = "Invalid numeric value '" + value + "' for Part.Lot property";
					throw new FilterExpressionException(lotErrorMessage, innerException4, "Part", property, operatorStr, value);
				}
			case "numberingseries":
			case "numbering series":
			{
				PartFilterExpressions.NumberingSeries numberingSeriesExpr = new PartFilterExpressions.NumberingSeries();
				StringConstantFilterExpression numberingSeriesValue = new StringConstantFilterExpression(value);
				StringOperatorType numberingSeriesOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(numberingSeriesExpr, numberingSeriesOp, numberingSeriesValue);
			}
			case "phase":
				try
				{
					PartFilterExpressions.Phase phaseExpr = new PartFilterExpressions.Phase();
					NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
				}
				catch (FormatException innerException3)
				{
					string phaseErrorMessage = "Invalid numeric value '" + value + "' for Part.Phase property";
					throw new FilterExpressionException(phaseErrorMessage, innerException3, "Part", property, operatorStr, value);
				}
			case "positionnumber":
			case "position number":
			{
				PartFilterExpressions.PositionNumber positionNumberExpr = new PartFilterExpressions.PositionNumber();
				StringConstantFilterExpression positionNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType positionNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(positionNumberExpr, positionNumberOp, positionNumberValue);
			}
			case "prefix":
			{
				PartFilterExpressions.Prefix prefixExpr = new PartFilterExpressions.Prefix();
				StringConstantFilterExpression prefixValue = new StringConstantFilterExpression(value);
				StringOperatorType prefixOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(prefixExpr, prefixOp, prefixValue);
			}
			case "primarypart":
			case "primary part":
			{
				PartFilterExpressions.PrimaryPart primaryPartExpr = new PartFilterExpressions.PrimaryPart();
				StringConstantFilterExpression primaryPartValue = new StringConstantFilterExpression(value);
				StringOperatorType primaryPartOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(primaryPartExpr, primaryPartOp, primaryPartValue);
			}
			case "startnumber":
			case "start number":
				try
				{
					PartFilterExpressions.StartNumber startNumberExpr = new PartFilterExpressions.StartNumber();
					NumericConstantFilterExpression startNumberValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType startNumberOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(startNumberExpr, startNumberOp, startNumberValue);
				}
				catch (FormatException innerException2)
				{
					string startNumberErrorMessage = "Invalid numeric value '" + value + "' for Part.StartNumber property";
					throw new FilterExpressionException(startNumberErrorMessage, innerException2, "Part", property, operatorStr, value);
				}
			case "pourphase":
			case "pour phase":
				try
				{
					PartFilterExpressions.PourPhase pourPhaseExpr = new PartFilterExpressions.PourPhase();
					NumericConstantFilterExpression pourPhaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType pourPhaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(pourPhaseExpr, pourPhaseOp, pourPhaseValue);
				}
				catch (FormatException innerException)
				{
					string pourPhaseErrorMessage = "Invalid numeric value '" + value + "' for Part.PourPhase property";
					throw new FilterExpressionException(pourPhaseErrorMessage, innerException, "Part", property, operatorStr, value);
				}
			default:
			{
				PartFilterExpressions.CustomString customExpr = new PartFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateAssemblyFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "name":
			{
				AssemblyFilterExpressions.Name nameExpr = new AssemblyFilterExpressions.Name();
				StringConstantFilterExpression nameValue = new StringConstantFilterExpression(value);
				StringOperatorType nameOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(nameExpr, nameOp, nameValue);
			}
			case "type":
			{
				AssemblyFilterExpressions.Type typeExpr = new AssemblyFilterExpressions.Type();
				StringConstantFilterExpression typeValue = new StringConstantFilterExpression(value);
				StringOperatorType typeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(typeExpr, typeOp, typeValue);
			}
			case "mainpartname":
			case "main part name":
			{
				TemplateFilterExpressions.CustomString mainPartNameExpr = new TemplateFilterExpressions.CustomString("MAINPART.NAME");
				StringConstantFilterExpression mainPartNameValue = new StringConstantFilterExpression(value);
				StringOperatorType mainPartNameOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(mainPartNameExpr, mainPartNameOp, mainPartNameValue);
			}
			case "idnumber":
			case "id number":
			{
				AssemblyFilterExpressions.IdNumber idNumberExpr = new AssemblyFilterExpressions.IdNumber();
				StringConstantFilterExpression idNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType idNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(idNumberExpr, idNumberOp, idNumberValue);
			}
			case "guid":
			{
				AssemblyFilterExpressions.Guid guidExpr = new AssemblyFilterExpressions.Guid();
				StringConstantFilterExpression guidValue = new StringConstantFilterExpression(value);
				StringOperatorType guidOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(guidExpr, guidOp, guidValue);
			}
			case "level":
			{
				AssemblyFilterExpressions.Level levelExpr = new AssemblyFilterExpressions.Level();
				StringConstantFilterExpression levelValue = new StringConstantFilterExpression(value);
				StringOperatorType levelOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(levelExpr, levelOp, levelValue);
			}
			case "phase":
			{
				AssemblyFilterExpressions.Phase phaseExpr = new AssemblyFilterExpressions.Phase();
				StringConstantFilterExpression phaseValue = new StringConstantFilterExpression(value);
				StringOperatorType phaseOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
			}
			case "positionnumber":
			case "position number":
			{
				AssemblyFilterExpressions.PositionNumber positionNumberExpr = new AssemblyFilterExpressions.PositionNumber();
				StringConstantFilterExpression positionNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType positionNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(positionNumberExpr, positionNumberOp, positionNumberValue);
			}
			case "prefix":
			{
				AssemblyFilterExpressions.Prefix prefixExpr = new AssemblyFilterExpressions.Prefix();
				StringConstantFilterExpression prefixValue = new StringConstantFilterExpression(value);
				StringOperatorType prefixOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(prefixExpr, prefixOp, prefixValue);
			}
			case "series":
			{
				AssemblyFilterExpressions.Series seriesExpr = new AssemblyFilterExpressions.Series();
				StringConstantFilterExpression seriesValue = new StringConstantFilterExpression(value);
				StringOperatorType seriesOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(seriesExpr, seriesOp, seriesValue);
			}
			case "startnumber":
			case "start number":
			{
				AssemblyFilterExpressions.StartNumber startNumberExpr = new AssemblyFilterExpressions.StartNumber();
				StringConstantFilterExpression startNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType startNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(startNumberExpr, startNumberOp, startNumberValue);
			}
			default:
			{
				AssemblyFilterExpressions.CustomString customExpr = new AssemblyFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateComponentFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "name":
			{
				ComponentFilterExpressions.Name nameExpr = new ComponentFilterExpressions.Name();
				StringConstantFilterExpression nameValue = new StringConstantFilterExpression(value);
				StringOperatorType nameOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(nameExpr, nameOp, nameValue);
			}
			case "connectioncode":
			case "connection code":
			{
				ComponentFilterExpressions.ConnectionCode connectionCodeExpr = new ComponentFilterExpressions.ConnectionCode();
				StringConstantFilterExpression connectionCodeValue = new StringConstantFilterExpression(value);
				StringOperatorType connectionCodeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(connectionCodeExpr, connectionCodeOp, connectionCodeValue);
			}
			case "phase":
				try
				{
					ComponentFilterExpressions.Phase phaseExpr = new ComponentFilterExpressions.Phase();
					NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
				}
				catch (FormatException innerException)
				{
					string phaseErrorMessage = "Invalid numeric value '" + value + "' for Component.Phase property";
					throw new FilterExpressionException(phaseErrorMessage, innerException, "Component", property, operatorStr, value);
				}
			case "runningnumber":
			case "running number":
			{
				ComponentFilterExpressions.RunningNumber runningNumberExpr = new ComponentFilterExpressions.RunningNumber();
				StringConstantFilterExpression runningNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType runningNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(runningNumberExpr, runningNumberOp, runningNumberValue);
			}
			case "isconceptual":
			case "is conceptual":
			{
				TemplateFilterExpressions.CustomString conceptualExpr = new TemplateFilterExpressions.CustomString("IS_CONCEPTUAL");
				StringConstantFilterExpression conceptualValue = new StringConstantFilterExpression(value);
				StringOperatorType conceptualOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(conceptualExpr, conceptualOp, conceptualValue);
			}
			default:
			{
				ComponentFilterExpressions.CustomString customExpr = new ComponentFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateBoltFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "name":
			{
				string nameErrorMessage = "Property 'Name' is not supported for Bolt category. Supported properties are: 'Hole1Type', 'Hole2Type', 'Hole3Type', 'Hole4Type', 'Hole5Type'";
				throw new FilterExpressionException(nameErrorMessage, "Bolt", property, operatorStr, value);
			}
			case "hole1type":
			case "hole 1 type":
			{
				TemplateFilterExpressions.CustomString hole1Expr = new TemplateFilterExpressions.CustomString("HOLE_1_TYPE");
				StringConstantFilterExpression hole1Value = new StringConstantFilterExpression(value);
				StringOperatorType hole1Op = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(hole1Expr, hole1Op, hole1Value);
			}
			case "hole2type":
			case "hole 2 type":
			{
				TemplateFilterExpressions.CustomString hole2Expr = new TemplateFilterExpressions.CustomString("HOLE_2_TYPE");
				StringConstantFilterExpression hole2Value = new StringConstantFilterExpression(value);
				StringOperatorType hole2Op = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(hole2Expr, hole2Op, hole2Value);
			}
			case "hole3type":
			case "hole 3 type":
			{
				TemplateFilterExpressions.CustomString hole3Expr = new TemplateFilterExpressions.CustomString("HOLE_3_TYPE");
				StringConstantFilterExpression hole3Value = new StringConstantFilterExpression(value);
				StringOperatorType hole3Op = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(hole3Expr, hole3Op, hole3Value);
			}
			case "hole4type":
			case "hole 4 type":
			{
				TemplateFilterExpressions.CustomString hole4Expr = new TemplateFilterExpressions.CustomString("HOLE_4_TYPE");
				StringConstantFilterExpression hole4Value = new StringConstantFilterExpression(value);
				StringOperatorType hole4Op = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(hole4Expr, hole4Op, hole4Value);
			}
			case "hole5type":
			case "hole 5 type":
			{
				TemplateFilterExpressions.CustomString hole5Expr = new TemplateFilterExpressions.CustomString("HOLE_5_TYPE");
				StringConstantFilterExpression hole5Value = new StringConstantFilterExpression(value);
				StringOperatorType hole5Op = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(hole5Expr, hole5Op, hole5Value);
			}
			default:
			{
				string boltErrorMessage = "Unsupported property '" + property + "' for Bolt category. Supported properties are: 'Hole1Type', 'Hole2Type', 'Hole3Type', 'Hole4Type', 'Hole5Type'";
				throw new FilterExpressionException(boltErrorMessage, "Bolt", property, operatorStr, value);
			}
			}
		}

		private static BinaryFilterExpression CreateReinforcingBarFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "name":
			{
				ReinforcingBarFilterExpressions.Name nameExpr = new ReinforcingBarFilterExpressions.Name();
				StringConstantFilterExpression nameValue = new StringConstantFilterExpression(value);
				StringOperatorType nameOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(nameExpr, nameOp, nameValue);
			}
			case "class":
				try
				{
					ReinforcingBarFilterExpressions.Class classExpr = new ReinforcingBarFilterExpressions.Class();
					NumericConstantFilterExpression classValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType classOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(classExpr, classOp, classValue);
				}
				catch (FormatException innerException5)
				{
					string rebarClassErrorMessage = "Invalid numeric value '" + value + "' for ReinforcingBar.Class property";
					throw new FilterExpressionException(rebarClassErrorMessage, innerException5, "ReinforcingBar", property, operatorStr, value);
				}
			case "diameter":
				try
				{
					ReinforcingBarFilterExpressions.Diameter diameterExpr = new ReinforcingBarFilterExpressions.Diameter();
					NumericConstantFilterExpression diameterValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType diameterOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(diameterExpr, diameterOp, diameterValue);
				}
				catch (FormatException innerException4)
				{
					string diameterErrorMessage = "Invalid numeric value '" + value + "' for ReinforcingBar.Diameter property";
					throw new FilterExpressionException(diameterErrorMessage, innerException4, "ReinforcingBar", property, operatorStr, value);
				}
			case "jointype":
			case "join type":
			{
				ReinforcingBarFilterExpressions.JoinType joinTypeExpr = new ReinforcingBarFilterExpressions.JoinType();
				StringConstantFilterExpression joinTypeValue = new StringConstantFilterExpression(value);
				StringOperatorType joinTypeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(joinTypeExpr, joinTypeOp, joinTypeValue);
			}
			case "length":
				try
				{
					ReinforcingBarFilterExpressions.Length lengthExpr = new ReinforcingBarFilterExpressions.Length();
					NumericConstantFilterExpression lengthValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType lengthOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(lengthExpr, lengthOp, lengthValue);
				}
				catch (FormatException innerException3)
				{
					string lengthErrorMessage = "Invalid numeric value '" + value + "' for ReinforcingBar.Length property";
					throw new FilterExpressionException(lengthErrorMessage, innerException3, "ReinforcingBar", property, operatorStr, value);
				}
			case "material":
			{
				ReinforcingBarFilterExpressions.Material materialExpr = new ReinforcingBarFilterExpressions.Material();
				StringConstantFilterExpression materialValue = new StringConstantFilterExpression(value);
				StringOperatorType materialOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(materialExpr, materialOp, materialValue);
			}
			case "numberingseries":
			case "numbering series":
			{
				ReinforcingBarFilterExpressions.NumberingSeries numberingSeriesExpr = new ReinforcingBarFilterExpressions.NumberingSeries();
				StringConstantFilterExpression numberingSeriesValue = new StringConstantFilterExpression(value);
				StringOperatorType numberingSeriesOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(numberingSeriesExpr, numberingSeriesOp, numberingSeriesValue);
			}
			case "phase":
				try
				{
					ReinforcingBarFilterExpressions.Phase phaseExpr = new ReinforcingBarFilterExpressions.Phase();
					NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
				}
				catch (FormatException innerException2)
				{
					string phaseErrorMessage = "Invalid numeric value '" + value + "' for ReinforcingBar.Phase property";
					throw new FilterExpressionException(phaseErrorMessage, innerException2, "ReinforcingBar", property, operatorStr, value);
				}
			case "position":
			{
				ReinforcingBarFilterExpressions.Position positionExpr = new ReinforcingBarFilterExpressions.Position();
				StringConstantFilterExpression positionValue = new StringConstantFilterExpression(value);
				StringOperatorType positionOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(positionExpr, positionOp, positionValue);
			}
			case "positionnumber":
			case "position number":
			{
				ReinforcingBarFilterExpressions.PositionNumber positionNumberExpr = new ReinforcingBarFilterExpressions.PositionNumber();
				StringConstantFilterExpression positionNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType positionNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(positionNumberExpr, positionNumberOp, positionNumberValue);
			}
			case "prefix":
			{
				ReinforcingBarFilterExpressions.Prefix prefixExpr = new ReinforcingBarFilterExpressions.Prefix();
				StringConstantFilterExpression prefixValue = new StringConstantFilterExpression(value);
				StringOperatorType prefixOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(prefixExpr, prefixOp, prefixValue);
			}
			case "shape":
			{
				ReinforcingBarFilterExpressions.Shape shapeExpr = new ReinforcingBarFilterExpressions.Shape();
				StringConstantFilterExpression shapeValue = new StringConstantFilterExpression(value);
				StringOperatorType shapeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(shapeExpr, shapeOp, shapeValue);
			}
			case "size":
			{
				ReinforcingBarFilterExpressions.Size sizeExpr = new ReinforcingBarFilterExpressions.Size();
				StringConstantFilterExpression sizeValue = new StringConstantFilterExpression(value);
				StringOperatorType sizeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(sizeExpr, sizeOp, sizeValue);
			}
			case "startnumber":
			case "start number":
				try
				{
					ReinforcingBarFilterExpressions.StartNumber startNumberExpr = new ReinforcingBarFilterExpressions.StartNumber();
					NumericConstantFilterExpression startNumberValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType startNumberOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(startNumberExpr, startNumberOp, startNumberValue);
				}
				catch (FormatException innerException)
				{
					string startNumberErrorMessage = "Invalid numeric value '" + value + "' for ReinforcingBar.StartNumber property";
					throw new FilterExpressionException(startNumberErrorMessage, innerException, "ReinforcingBar", property, operatorStr, value);
				}
			default:
			{
				ReinforcingBarFilterExpressions.CustomString customExpr = new ReinforcingBarFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateSurfaceFilterExpression(string property, string operatorStr, string value)
		{
			string text = property.ToLower();
			string text2 = text;
			if (!(text2 == "name"))
			{
				if (text2 == "type")
				{
					SurfaceFilterExpressions.Type typeExpr = new SurfaceFilterExpressions.Type();
					StringConstantFilterExpression typeValue = new StringConstantFilterExpression(value);
					StringOperatorType typeOp = GetStringOperator(operatorStr);
					return new BinaryFilterExpression(typeExpr, typeOp, typeValue);
				}
				SurfaceFilterExpressions.CustomString customExpr = new SurfaceFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			SurfaceFilterExpressions.Name nameExpr = new SurfaceFilterExpressions.Name();
			StringConstantFilterExpression nameValue = new StringConstantFilterExpression(value);
			StringOperatorType nameOp = GetStringOperator(operatorStr);
			return new BinaryFilterExpression(nameExpr, nameOp, nameValue);
		}

		private static BinaryFilterExpression CreateReferenceObjectFilterExpression(string property, string operatorStr, string value)
		{
			ReferenceObjectFilterExpressions.CustomString customExpr = new ReferenceObjectFilterExpressions.CustomString(property);
			StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
			StringOperatorType customOp = GetStringOperator(operatorStr);
			return new BinaryFilterExpression(customExpr, customOp, customValue);
		}

		private static BinaryFilterExpression CreatePourObjectFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "pournumber":
			case "pour number":
			{
				PourObjectFilterExpressions.PourNumber pourNumberExpr = new PourObjectFilterExpressions.PourNumber();
				StringConstantFilterExpression pourNumberValue = new StringConstantFilterExpression(value);
				StringOperatorType pourNumberOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(pourNumberExpr, pourNumberOp, pourNumberValue);
			}
			case "pourtype":
			case "pour type":
			{
				PourObjectFilterExpressions.PourType pourTypeExpr = new PourObjectFilterExpressions.PourType();
				StringConstantFilterExpression pourTypeValue = new StringConstantFilterExpression(value);
				StringOperatorType pourTypeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(pourTypeExpr, pourTypeOp, pourTypeValue);
			}
			case "concretemixture":
			case "concrete mixture":
			{
				PourObjectFilterExpressions.ConcreteMixture concreteMixtureExpr = new PourObjectFilterExpressions.ConcreteMixture();
				StringConstantFilterExpression concreteMixtureValue = new StringConstantFilterExpression(value);
				StringOperatorType concreteMixtureOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(concreteMixtureExpr, concreteMixtureOp, concreteMixtureValue);
			}
			case "material":
			{
				PourObjectFilterExpressions.Material materialExpr = new PourObjectFilterExpressions.Material();
				StringConstantFilterExpression materialValue = new StringConstantFilterExpression(value);
				StringOperatorType materialOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(materialExpr, materialOp, materialValue);
			}
			case "pourphase":
			case "pour phase":
			{
				PourObjectFilterExpressions.PourPhase pourPhaseExpr = new PourObjectFilterExpressions.PourPhase();
				StringConstantFilterExpression pourPhaseValue = new StringConstantFilterExpression(value);
				StringOperatorType pourPhaseOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(pourPhaseExpr, pourPhaseOp, pourPhaseValue);
			}
			default:
			{
				PourObjectFilterExpressions.CustomString customExpr = new PourObjectFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreatePourUnitFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "name":
			{
				PourUnitFilterExpressions.Name nameExpr = new PourUnitFilterExpressions.Name();
				StringConstantFilterExpression nameValue = new StringConstantFilterExpression(value);
				StringOperatorType nameOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(nameExpr, nameOp, nameValue);
			}
			case "guid":
			{
				PourUnitFilterExpressions.Guid guidExpr = new PourUnitFilterExpressions.Guid();
				StringConstantFilterExpression guidValue = new StringConstantFilterExpression(value);
				StringOperatorType guidOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(guidExpr, guidOp, guidValue);
			}
			case "assignmenttype":
			case "assignment type":
			{
				TemplateFilterExpressions.CustomString assignmentTypeExpr = new TemplateFilterExpressions.CustomString("ADDED_TO_POUR_UNIT");
				StringConstantFilterExpression assignmentTypeValue = new StringConstantFilterExpression(value);
				StringOperatorType assignmentTypeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(assignmentTypeExpr, assignmentTypeOp, assignmentTypeValue);
			}
			default:
			{
				PourUnitFilterExpressions.CustomString customExpr = new PourUnitFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateConstructionObjectFilterExpression(string property, string operatorStr, string value)
		{
			string text = property.ToLower();
			string text2 = text;
			if (!(text2 == "phase"))
			{
				if (text2 == "type")
				{
					try
					{
						ConstructionObjectFilterExpressions.Type typeExpr = new ConstructionObjectFilterExpressions.Type();
						NumericConstantFilterExpression typeValue = new NumericConstantFilterExpression(GetObjectTypeNumericValue(value));
						NumericOperatorType typeOp = GetNumericOperator(operatorStr);
						return new BinaryFilterExpression(typeExpr, typeOp, typeValue);
					}
					catch (FormatException innerException)
					{
						string typeErrorMessage = "Invalid numeric value '" + value + "' for ConstructionObject.Type property";
						throw new FilterExpressionException(typeErrorMessage, innerException, "ConstructionObject", property, operatorStr, value);
					}
				}
				ConstructionObjectFilterExpressions.CustomString customExpr = new ConstructionObjectFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			try
			{
				ConstructionObjectFilterExpressions.Phase phaseExpr = new ConstructionObjectFilterExpressions.Phase();
				NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
				NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
				return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
			}
			catch (FormatException innerException2)
			{
				string phaseErrorMessage = "Invalid numeric value '" + value + "' for ConstructionObject.Phase property";
				throw new FilterExpressionException(phaseErrorMessage, innerException2, "ConstructionObject", property, operatorStr, value);
			}
		}

		private static BinaryFilterExpression CreateLoadFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "group":
			{
				LoadFilterExpressions.Group groupExpr = new LoadFilterExpressions.Group();
				StringConstantFilterExpression groupValue = new StringConstantFilterExpression(value);
				StringOperatorType groupOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(groupExpr, groupOp, groupValue);
			}
			case "phase":
				try
				{
					LoadFilterExpressions.Phase phaseExpr = new LoadFilterExpressions.Phase();
					NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
				}
				catch (FormatException innerException)
				{
					string phaseErrorMessage = "Invalid numeric value '" + value + "' for Load.Phase property";
					throw new FilterExpressionException(phaseErrorMessage, innerException, "Load", property, operatorStr, value);
				}
			case "type":
			{
				LoadFilterExpressions.Type typeExpr = new LoadFilterExpressions.Type();
				StringConstantFilterExpression typeValue = new StringConstantFilterExpression(value);
				StringOperatorType typeOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(typeExpr, typeOp, typeValue);
			}
			default:
			{
				LoadFilterExpressions.CustomString customExpr = new LoadFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateWeldFilterExpression(string property, string operatorStr, string value)
		{
			switch (property.ToLower())
			{
			case "phase":
				try
				{
					WeldFilterExpressions.Phase phaseExpr = new WeldFilterExpressions.Phase();
					NumericConstantFilterExpression phaseValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType phaseOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(phaseExpr, phaseOp, phaseValue);
				}
				catch (FormatException innerException6)
				{
					string phaseErrorMessage = "Invalid numeric value '" + value + "' for Weld.Phase property";
					throw new FilterExpressionException(phaseErrorMessage, innerException6, "Weld", property, operatorStr, value);
				}
			case "positionnumber":
			case "position number":
			{
				WeldFilterExpressions.PositionNumber positionExpr = new WeldFilterExpressions.PositionNumber();
				StringConstantFilterExpression positionValue = new StringConstantFilterExpression(value);
				StringOperatorType positionOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(positionExpr, positionOp, positionValue);
			}
			case "referencetext":
			case "reference text":
			{
				WeldFilterExpressions.ReferenceText refTextExpr = new WeldFilterExpressions.ReferenceText();
				StringConstantFilterExpression refTextValue = new StringConstantFilterExpression(value);
				StringOperatorType refTextOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(refTextExpr, refTextOp, refTextValue);
			}
			case "sizeaboveline":
			case "size above line":
				try
				{
					WeldFilterExpressions.SizeAboveLine sizeAboveExpr = new WeldFilterExpressions.SizeAboveLine();
					NumericConstantFilterExpression sizeAboveValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType sizeAboveOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(sizeAboveExpr, sizeAboveOp, sizeAboveValue);
				}
				catch (FormatException innerException5)
				{
					string sizeAboveErrorMessage = "Invalid numeric value '" + value + "' for Weld.SizeAboveLine property";
					throw new FilterExpressionException(sizeAboveErrorMessage, innerException5, "Weld", property, operatorStr, value);
				}
			case "sizebelowline":
			case "size below line":
				try
				{
					WeldFilterExpressions.SizeBelowLine sizeBelowExpr = new WeldFilterExpressions.SizeBelowLine();
					NumericConstantFilterExpression sizeBelowValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType sizeBelowOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(sizeBelowExpr, sizeBelowOp, sizeBelowValue);
				}
				catch (FormatException innerException4)
				{
					string sizeBelowErrorMessage = "Invalid numeric value '" + value + "' for Weld.SizeBelowLine property";
					throw new FilterExpressionException(sizeBelowErrorMessage, innerException4, "Weld", property, operatorStr, value);
				}
			case "typeaboveline":
			case "type above line":
				try
				{
					WeldFilterExpressions.TypeAboveLine typeAboveExpr = new WeldFilterExpressions.TypeAboveLine();
					NumericConstantFilterExpression typeAboveValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType typeAboveOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(typeAboveExpr, typeAboveOp, typeAboveValue);
				}
				catch (FormatException innerException3)
				{
					string typeAboveErrorMessage = "Invalid numeric value '" + value + "' for Weld.TypeAboveLine property";
					throw new FilterExpressionException(typeAboveErrorMessage, innerException3, "Weld", property, operatorStr, value);
				}
			case "typebelowline":
			case "type below line":
				try
				{
					WeldFilterExpressions.TypeBelowLine typeBelowExpr = new WeldFilterExpressions.TypeBelowLine();
					NumericConstantFilterExpression typeBelowValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType typeBelowOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(typeBelowExpr, typeBelowOp, typeBelowValue);
				}
				catch (FormatException innerException2)
				{
					string typeBelowErrorMessage = "Invalid numeric value '" + value + "' for Weld.TypeBelowLine property";
					throw new FilterExpressionException(typeBelowErrorMessage, innerException2, "Weld", property, operatorStr, value);
				}
			case "weldingsite":
			case "welding site":
				try
				{
					WeldFilterExpressions.WeldingSite weldingSiteExpr = new WeldFilterExpressions.WeldingSite();
					NumericConstantFilterExpression weldingSiteValue = new NumericConstantFilterExpression(double.Parse(value));
					NumericOperatorType weldingSiteOp = GetNumericOperator(operatorStr);
					return new BinaryFilterExpression(weldingSiteExpr, weldingSiteOp, weldingSiteValue);
				}
				catch (FormatException innerException)
				{
					string weldingSiteErrorMessage = "Invalid numeric value '" + value + "' for Weld.WeldingSite property";
					throw new FilterExpressionException(weldingSiteErrorMessage, innerException, "Weld", property, operatorStr, value);
				}
			default:
			{
				WeldFilterExpressions.CustomString customExpr = new WeldFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(value);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static BinaryFilterExpression CreateLocationBreakdownStructureFilterExpression(string property, string operatorStr, string value)
		{
			string quotedValue = "\"" + value + "\"";
			switch (property.ToLower())
			{
			case "floor":
			case "storey":
			case "story":
			{
				LogicalAreaFilterExpressions.Story storyExpr = new LogicalAreaFilterExpressions.Story();
				StringConstantFilterExpression storyValue = new StringConstantFilterExpression(quotedValue);
				StringOperatorType storyOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(storyExpr, storyOp, storyValue);
			}
			case "building":
			{
				LogicalAreaFilterExpressions.Building buildingExpr = new LogicalAreaFilterExpressions.Building();
				StringConstantFilterExpression buildingValue = new StringConstantFilterExpression(quotedValue);
				StringOperatorType buildingOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(buildingExpr, buildingOp, buildingValue);
			}
			case "section":
			{
				LogicalAreaFilterExpressions.Section sectionExpr = new LogicalAreaFilterExpressions.Section();
				StringConstantFilterExpression sectionValue = new StringConstantFilterExpression(quotedValue);
				StringOperatorType sectionOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(sectionExpr, sectionOp, sectionValue);
			}
			case "site":
			{
				LogicalAreaFilterExpressions.Site siteExpr = new LogicalAreaFilterExpressions.Site();
				StringConstantFilterExpression siteValue = new StringConstantFilterExpression(quotedValue);
				StringOperatorType siteOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(siteExpr, siteOp, siteValue);
			}
			default:
			{
				LogicalAreaFilterExpressions.CustomString customExpr = new LogicalAreaFilterExpressions.CustomString(property);
				StringConstantFilterExpression customValue = new StringConstantFilterExpression(quotedValue);
				StringOperatorType customOp = GetStringOperator(operatorStr);
				return new BinaryFilterExpression(customExpr, customOp, customValue);
			}
			}
		}

		private static StringOperatorType GetStringOperator(string operatorStr)
		{
			switch (operatorStr.ToUpper())
			{
			case "IS_EQUAL":
				return StringOperatorType.IS_EQUAL;
			case "IS_NOT_EQUAL":
				return StringOperatorType.IS_NOT_EQUAL;
			case "CONTAINS":
				return StringOperatorType.CONTAINS;
			case "NOT_CONTAINS":
				return StringOperatorType.NOT_CONTAINS;
			case "STARTS_WITH":
				return StringOperatorType.STARTS_WITH;
			case "NOT_STARTS_WITH":
				return StringOperatorType.NOT_STARTS_WITH;
			case "ENDS_WITH":
				return StringOperatorType.ENDS_WITH;
			case "NOT_ENDS_WITH":
				return StringOperatorType.NOT_ENDS_WITH;
			default:
				return StringOperatorType.IS_EQUAL;
			}
		}

		private static NumericOperatorType GetNumericOperator(string operatorStr)
		{
			switch (operatorStr.ToUpper())
			{
			case "IS_EQUAL":
				return NumericOperatorType.IS_EQUAL;
			case "IS_NOT_EQUAL":
				return NumericOperatorType.IS_NOT_EQUAL;
			case "GREATER_THAN":
				return NumericOperatorType.GREATER_THAN;
			case "SMALLER_THAN":
				return NumericOperatorType.SMALLER_THAN;
			case "GREATER_OR_EQUAL":
				return NumericOperatorType.GREATER_OR_EQUAL;
			case "SMALLER_OR_EQUAL":
				return NumericOperatorType.SMALLER_OR_EQUAL;
			default:
				return NumericOperatorType.IS_EQUAL;
			}
		}

		private static double GetObjectTypeNumericValue(string value)
		{
			if (double.TryParse(value, out var numericValue))
			{
				return numericValue;
			}
			if (Enum.TryParse<TeklaStructuresDatabaseTypeEnum>(value, true, out var enumValue))
			{
				return (double)enumValue;
			}
			switch (value.ToUpper())
			{
			case "BEAM":
			case "COLUMN":
			case "PLATE":
			case "STEEL":
			case "CONCRETE":
				return 2.0;
			case "REINFORCEMENT":
				return 47.0;
			default:
				return 2.0;
			}
		}
	}
}
